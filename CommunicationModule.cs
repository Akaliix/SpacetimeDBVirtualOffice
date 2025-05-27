using SpacetimeDB;

public static partial class CommunicationModule
{
    [Table(Name = "chat_message", Public = true)]
    public partial struct ChatMessage
    {
        [PrimaryKey, AutoInc]
        public uint message_id;

        public uint sender_user_id;
        public string sender_username;

        [SpacetimeDB.Index.BTree]
        public uint room_id;

        public string content;
        public bool shout;
        public ulong timestamp;
    }

    [Table(Name = "voice_clip", Public = true)]
    public partial struct VoiceClip
    {
        [PrimaryKey]
        public uint sender_user_id;

        [SpacetimeDB.Index.BTree]
        public uint room_id;

        public string sender_username;
        public ulong timestamp;     // when recorded (microseconds)
        public byte[] audio_data;   // raw PCM WAV bytes
    }

    [Table(Name = "images", Public = true)]
    public partial struct Images
    {
        [PrimaryKey]
        public string building_identifier; // Unique identifier for the building

        [SpacetimeDB.Index.BTree]
        public uint room_id;

        public ulong timestamp;

        public uint sender_user_id; // The user ID of the player who is broadcasting the image
        public string sender_username;

        public int width;  // Width of the image
        public int height; // Height of the image
        public byte[] image_data; // raw image bytes
    }

    [Table(Name = "image_broadcast_lock", Public = true)]
    public partial struct ImageBroadcastLock
    {
        [PrimaryKey]
        public string building_identifier; // Unique identifier for the image broadcast lock

        [SpacetimeDB.Index.BTree]
        public uint sender_user_id; // The user ID of the player who is broadcasting the image

        public string sender_username;
        public ulong timestamp; // when recorded (microseconds)
    }

    [Reducer]
    public static void SendMessage(ReducerContext ctx, string content, bool shout)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var sender = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (sender.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send a message");

        // Validate message content
        if (string.IsNullOrWhiteSpace(content) || content.Length > 500)
            throw new Exception("Message must be between 1 and 500 characters");

        ctx.Db.chat_message.Insert(new ChatMessage
        {
            sender_user_id = user_id,
            sender_username = sender.username,
            room_id = sender.room_id,
            content = content.Trim(),
            shout = shout,
            timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
        });
    }

    [Reducer]
    public static void SendVoice(ReducerContext ctx, byte[] audio_data)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send a voice clip");

        // Validate audio data
        if (audio_data == null || audio_data.Length == 0 || audio_data.Length > 1024 * 1024) // 1MB limit
            throw new Exception("Invalid audio data size");

        // If user already has a voice clip, update it, else insert
        var existingClip = ctx.Db.voice_clip.sender_user_id.Find(user_id);

        if (existingClip != null)
        {
            var updatedClip = existingClip.Value;
            updatedClip.audio_data = audio_data;
            updatedClip.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            updatedClip.room_id = player.room_id;
            ctx.Db.voice_clip.sender_user_id.Update(updatedClip);
        }
        else
        {
            ctx.Db.voice_clip.Insert(new VoiceClip
            {
                sender_user_id = user_id,
                sender_username = player.username,
                room_id = player.room_id,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
                audio_data = audio_data
            });
        }
    }

    [Reducer]
    public static void SendImage(ReducerContext ctx, string building_identifier, byte[] image_data, int width, int height)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send an image");

        // Validate input
        if (string.IsNullOrWhiteSpace(building_identifier) || building_identifier.Length > 100)
            throw new Exception("Invalid building identifier");

        if (image_data == null || image_data.Length == 0 || image_data.Length > 5 * 1024 * 1024) // 5MB limit
            throw new Exception("Invalid image data size");

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new Exception("Invalid image dimensions");

        // If image exists, update it, else insert
        var existingImage = ctx.Db.images.building_identifier.Find(building_identifier);
        if (existingImage != null)
        {
            if (existingImage.Value.room_id != player.room_id)
                throw new Exception("Cannot update image for a building that is not in the same room");

            var updatedImage = existingImage.Value;
            updatedImage.image_data = image_data;
            updatedImage.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            updatedImage.sender_user_id = user_id;
            updatedImage.sender_username = player.username;
            updatedImage.width = width;
            updatedImage.height = height;
            ctx.Db.images.building_identifier.Update(updatedImage);
        }
        else
        {
            ctx.Db.images.Insert(new Images
            {
                building_identifier = building_identifier,
                room_id = player.room_id,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
                sender_user_id = user_id,
                sender_username = player.username,
                width = width,
                height = height,
                image_data = image_data
            });
        }
    }

    [Reducer]
    public static void LockImageBroadcast(ReducerContext ctx, string building_identifier)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to lock image broadcast");

        // Validate input
        if (string.IsNullOrWhiteSpace(building_identifier))
            throw new Exception("Building identifier is required");

        // Make sure image exists and player is in the same room
        var image = ctx.Db.images.building_identifier.Find(building_identifier) ?? throw new Exception("Image not found");
        if (image.room_id != player.room_id)
            throw new Exception("Cannot lock image broadcast for a building that is not in the same room");

        // If lock exists, update it, else insert
        var existingLock = ctx.Db.image_broadcast_lock.building_identifier.Find(building_identifier);
        if (existingLock != null)
        {
            var updatedLock = existingLock.Value;
            updatedLock.sender_user_id = user_id;
            updatedLock.sender_username = player.username;
            updatedLock.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.image_broadcast_lock.building_identifier.Update(updatedLock);
        }
        else
        {
            ctx.Db.image_broadcast_lock.Insert(new ImageBroadcastLock
            {
                building_identifier = building_identifier,
                sender_user_id = user_id,
                sender_username = player.username,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }
    }

    // Helper function to update communication records when username changes
    public static void UpdateCommunicationUsername(ReducerContext ctx, uint user_id, string new_username)
    {
        // Update recent chat messages (last 24 hours) with new username
        ulong cutoffTime = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch - (24UL * 60UL * 60UL * 1000000UL); // 24 hours ago

        foreach (var message in ctx.Db.chat_message.Iter())
        {
            if (message.sender_user_id == user_id && message.timestamp > cutoffTime)
            {
                var updatedMessage = message;
                updatedMessage.sender_username = new_username;
                ctx.Db.chat_message.message_id.Update(updatedMessage);
            }
        }

        // Update voice clips with new username
        var voiceClip = ctx.Db.voice_clip.sender_user_id.Find(user_id);
        if (voiceClip != null)
        {
            var updatedVoice = voiceClip.Value;
            updatedVoice.sender_username = new_username;
            ctx.Db.voice_clip.sender_user_id.Update(updatedVoice);
        }

        // Update image records with new username
        foreach (var image in ctx.Db.images.Iter())
        {
            if (image.sender_user_id == user_id)
            {
                var updatedImage = image;
                updatedImage.sender_username = new_username;
                ctx.Db.images.building_identifier.Update(updatedImage);
            }
        }

        // Update image broadcast locks with new username
        foreach (var lockRecord in ctx.Db.image_broadcast_lock.Iter())
        {
            if (lockRecord.sender_user_id == user_id)
            {
                var updatedLock = lockRecord;
                updatedLock.sender_username = new_username;
                ctx.Db.image_broadcast_lock.building_identifier.Update(updatedLock);
            }
        }
    }
}