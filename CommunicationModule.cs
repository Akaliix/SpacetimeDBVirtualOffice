using SpacetimeDB;

public static partial class CommunicationModule
{
    [Table(Name = "chat_message", Public = true)]
    public partial struct ChatMessage
    {
        [PrimaryKey, AutoInc]
        public uint message_id;

        public Identity sender;

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
        public Identity sender;

        [SpacetimeDB.Index.BTree]
        public uint room_id;

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

        public Identity sender; // The identity of the player who is broadcasting the image

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
        public Identity sender; // The identity of the player who is broadcasting the image
        public ulong timestamp; // when recorded (microseconds)
    }

    [Reducer]
    public static void SendMessage(ReducerContext ctx, string content, bool shout)
    {
        var sender = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (sender.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send a message");

        ctx.Db.chat_message.Insert(new ChatMessage
        {
            sender = ctx.Sender,
            room_id = sender.room_id,
            content = content,
            shout = shout,
            timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
        });
    }

    [Reducer]
    public static void SendVoice(ReducerContext ctx, byte[] audio_data)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send a voice clip");

        // If identity is in voice table, update it else insert
        var existingClip = ctx.Db.voice_clip.sender.Find(ctx.Sender);

        if (existingClip != null)
        {
            VoiceClip existingClipValue = existingClip.Value;
            existingClipValue.audio_data = audio_data;
            existingClipValue.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.voice_clip.sender.Update(existingClipValue);
        }
        else
        {
            ctx.Db.voice_clip.Insert(new VoiceClip
            {
                sender = ctx.Sender,
                room_id = player.room_id,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
                audio_data = audio_data
            });
        }
    }

    [Reducer]
    public static void SendImage(ReducerContext ctx, string building_identifier, byte[] image_data, int width, int height)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to send an image");
        // If identity is in image table, update it else insert
        var existingImage = ctx.Db.images.building_identifier.Find(building_identifier);
        if (existingImage != null)
        {
            if (existingImage.Value.room_id != player.room_id)
                throw new Exception("Cannot update image for a building that is not in the same room");

            Images existingImageValue = existingImage.Value;
            existingImageValue.image_data = image_data;
            existingImageValue.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            existingImageValue.sender = ctx.Sender;
            existingImageValue.width = width;
            existingImageValue.height = height;
            ctx.Db.images.building_identifier.Update(existingImageValue);
        }
        else
        {
            ctx.Db.images.Insert(new Images
            {
                building_identifier = building_identifier,
                room_id = player.room_id,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
                sender = ctx.Sender,
                width = width,
                height = height,
                image_data = image_data
            });
        }
    }

    [Reducer]
    public static void LockImageBroadcast(ReducerContext ctx, string building_identifier)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must be in a room to lock image broadcast");

        // make sure player is in the same room as image
        Images images = ctx.Db.images.building_identifier.Find(building_identifier) ?? throw new Exception("Image not found");
        if (images.room_id != player.room_id)
            throw new Exception("Cannot lock image broadcast for a building that is not in the same room");

        // If identity is in image table, update it else insert
        var existingLock = ctx.Db.image_broadcast_lock.building_identifier.Find(building_identifier);
        if (existingLock != null)
        {
            ImageBroadcastLock existingLockValue = existingLock.Value;
            existingLockValue.sender = ctx.Sender;
            existingLockValue.timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.image_broadcast_lock.building_identifier.Update(existingLockValue);
        }
        else
        {
            ctx.Db.image_broadcast_lock.Insert(new ImageBroadcastLock
            {
                building_identifier = building_identifier,
                sender = ctx.Sender,
                timestamp = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }
    }
}