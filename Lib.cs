using SpacetimeDB;

public static partial class Module
{
    [Table(Name = "online_player", Public = true)]
    public partial struct OnlinePlayer
    {
        [PrimaryKey]
        public Identity identity;

        [Unique, AutoInc]
        public uint player_id;

        public string name;
        public string color;

        [SpacetimeDB.Index.BTree]
        public uint room_id;
        public ulong last_room_join_time; // Time when the player joined the room

        public ulong last_connect_time; // Time when the player joined the room
        public ulong total_play_time; // Total time spent in the room

        public DbVector3 last_position;
        public float last_rotation;
    }

    [Table(Name = "player_room_position", Public = true)]
    public partial struct PlayerRoomPosition
    {
        [PrimaryKey]
        public string identity_room_key; // Example: "playerIdentity|roomId"

        public Identity identity;
        public uint room_id;
        public DbVector3 last_position;
        public float last_rotation;
    }

    [Table(Name = "player_count", Public = true)]
    public partial struct PlayerCount
    {
        [PrimaryKey]
        public uint id; // always 0
        public uint count;
    }

    [Table(Name = "logged_out_player")]
    public partial struct LoggedOutPlayer
    {
        [PrimaryKey]
        public Identity identity;

        public uint player_id;
        public string name;
        public string color;
        public ulong last_disconnect_time; // Time when the player left the room
        public ulong total_play_time; // Total time spent in the room
    }

    [Table(Name = "game_room", Public = true)]
    public partial struct GameRoom
    {
        [PrimaryKey, AutoInc]
        public uint room_id;
        public string name;
        // Password is no longer included here
    }

    [Table(Name = "game_room_secret")]
    public partial struct GameRoomSecret
    {
        [PrimaryKey]
        public uint room_id;
        public string password;
    }

    [Table(Name = "room_entity", Public = true)]
    public partial struct RoomEntity
    {
        [PrimaryKey]
        public uint room_id;
        public Identity identity; // The identity of the player who updated the entity
        public string data;
    }

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


    // -------------------------
    // Reducers
    // -------------------------

    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("Server initialized.");
        ctx.Db.player_count.Insert(new PlayerCount { id = 0, count = 0 });
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void Connect(ReducerContext ctx)
    {
        var count = ctx.Db.player_count.id.Find(0) ?? throw new Exception("Player count not initialized");

        var player = ctx.Db.logged_out_player.identity.Find(ctx.Sender);
        if (player != null)
        {
            ctx.Db.online_player.Insert(new OnlinePlayer
            {
                identity = player.Value.identity,
                player_id = player.Value.player_id,
                name = player.Value.name,
                color = player.Value.color,
                room_id = uint.MaxValue,
                last_position = new DbVector3(0, 0, 0),
                last_rotation = 0,
                last_connect_time = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
            ctx.Db.logged_out_player.identity.Delete(player.Value.identity);
        }
        else
        {
            ctx.Db.online_player.Insert(new OnlinePlayer
            {
                identity = ctx.Sender,
                name = "guest",
                color = "#FFFFFF",
                room_id = uint.MaxValue,
                last_position = new DbVector3(0, 0, 0),
                last_rotation = 0,
                last_connect_time = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }

        count.count += 1;
        ctx.Db.player_count.id.Update(count);
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void Disconnect(ReducerContext ctx)
    {
        var count = ctx.Db.player_count.id.Find(0) ?? throw new Exception("Player count not initialized");

        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");

        ulong currentTime = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
        ctx.Db.logged_out_player.Insert(new LoggedOutPlayer
        {
            identity = player.identity,
            player_id = player.player_id,
            name = player.name,
            color = player.color,
            last_disconnect_time = currentTime,
            total_play_time = player.total_play_time + (currentTime - player.last_connect_time)
        });
        ctx.Db.online_player.identity.Delete(player.identity);

        count.count -= 1;
        ctx.Db.player_count.id.Update(count);
    }

    public static string MakeKey(Identity identity, uint roomId) => $"{identity.ToString()}|{roomId}";

    [Reducer]
    public static void CreateRoom(ReducerContext ctx, string room_name, string password)
    {
        foreach (var room in ctx.Db.game_room.Iter())
        {
            if (room.name == room_name)
                throw new Exception("Room name already exists");
        }

        var newRoom = new GameRoom
        {
            name = room_name
        };

        var result = ctx.Db.game_room.Insert(newRoom);

        // Insert the password into the private table
        ctx.Db.game_room_secret.Insert(new GameRoomSecret
        {
            room_id = result.room_id,
            password = password
        });
    }

    [Reducer]
    public static void JoinRoom(ReducerContext ctx, uint room_id, string password)
    {
        var room = ctx.Db.game_room.room_id.Find(room_id) ?? throw new Exception("Room not found");
        var roomSecret = ctx.Db.game_room_secret.room_id.Find(room_id) ?? throw new Exception("Room secret not found");
        if (roomSecret.password != password)
            throw new Exception("Incorrect password");

        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        player.room_id = room_id;
        var key = $"{ctx.Sender}|{room_id}";
        var savedPos = ctx.Db.player_room_position.identity_room_key.Find(key);

        player.room_id = room_id;
        player.last_position = savedPos?.last_position ?? new DbVector3(0, 0, 0);
        player.last_rotation = savedPos?.last_rotation ?? 0;
        player.last_room_join_time = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
        ctx.Db.online_player.identity.Update(player);
    }

    [Reducer]
    public static void LeaveRoom(ReducerContext ctx)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player is not in a room");

        // Save the player's position in the room
        var key = $"{ctx.Sender}|{player.room_id}";
        var savedPos = ctx.Db.player_room_position.identity_room_key.Find(key);
        if (savedPos != null)
        {
            PlayerRoomPosition savedPoss = savedPos.Value;
            savedPoss.last_position = player.last_position;
            savedPoss.last_rotation = player.last_rotation;
            ctx.Db.player_room_position.identity_room_key.Update(savedPoss);
        }
        else
        {
            ctx.Db.player_room_position.Insert(new PlayerRoomPosition
            {
                identity = ctx.Sender,
                room_id = player.room_id,
                last_position = player.last_position,
                last_rotation = player.last_rotation,
                identity_room_key = key
            });
        }

        // log
        Log.Info($"Player: {player.player_id} - has left room {player.room_id}");

        player.room_id = uint.MaxValue;
        player.last_position = new DbVector3(0, 0, 0);
        player.last_rotation = 0;
        ctx.Db.online_player.identity.Update(player);
    }

    [Reducer]
    public static void SetPlayerProfile(ReducerContext ctx, string name, string color)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        player.name = name;
        player.color = color;
        ctx.Db.online_player.identity.Update(player);
    }

    [Reducer]
    public static void UpdateLastPosition(ReducerContext ctx, DbVector3 position, float rotation)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must join a room first");
        player.last_position = position;
        player.last_rotation = rotation;
        ctx.Db.online_player.identity.Update(player);
    }

    [Reducer]
    public static void SaveEntity(ReducerContext ctx, uint room_id, string data)
    {
        var room = ctx.Db.game_room.room_id.Find(room_id) ?? throw new Exception("Room not found");
        // Check if the entity already exists
        var existingEntity = ctx.Db.room_entity.room_id.Find(room_id);
        if (existingEntity != null)
        {
            // Update the existing entity
            RoomEntity existingEntityValue = existingEntity.Value;
            existingEntityValue.data = data;
            existingEntityValue.identity = ctx.Sender;
            ctx.Db.room_entity.room_id.Update(existingEntityValue);
        }
        else
        {
            // Insert a new entity
            ctx.Db.room_entity.Insert(new RoomEntity
            {
                room_id = room_id,
                identity = ctx.Sender,
                data = data
            });
        }
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
}
