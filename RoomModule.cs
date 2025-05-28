using SpacetimeDB;

public static partial class RoomModule
{
    [Table(Name = "game_room", Public = true)]
    public partial struct GameRoom
    {
        [PrimaryKey, AutoInc]
        public uint room_id;
        public string name;
        public uint created_by_user_id; // Track who created the room
        public ulong created_at;
        public string password; // Added password field
    }

    [Table(Name = "room_entity", Public = true)]
    public partial struct RoomEntity
    {
        [PrimaryKey]
        public uint room_id;
        public uint user_id; // The user who updated the entity
        public string data;
        public ulong last_updated;
    }

    [Table(Name = "player_room_position")]
    public partial struct PlayerRoomPosition
    {
        [PrimaryKey]
        public string user_room_key; // Example: "userId|roomId"

        public uint user_id;
        public uint room_id;
        public DbVector3 last_position;
        public float last_rotation;
        public ulong last_updated;
    }

    [Reducer]
    public static void CreateRoom(ReducerContext ctx, string room_name, string password)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Validate input
        if (string.IsNullOrWhiteSpace(room_name) || room_name.Length < 3 || room_name.Length > 50)
            throw new Exception("Room name must be between 3 and 50 characters");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 3)
            throw new Exception("Password must be at least 3 characters");

        foreach (var room in ctx.Db.game_room.Iter())
        {
            if (room.name == room_name)
                throw new Exception("Room name already exists");
        }

        var newRoom = new GameRoom
        {
            name = room_name,
            created_by_user_id = user_id,
            created_at = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
            password = password // Store password in game_room
        };

        ctx.Db.game_room.Insert(newRoom);

        Log.Info($"Room created: {room_name} by user {user_id}");
    }

    [Reducer]
    public static void JoinRoom(ReducerContext ctx, string room_name, string password)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Find the room by name
        GameRoom? room = null;
        foreach (var r in ctx.Db.game_room.Iter())
        {
            if (r.name == room_name)
            {
                room = r;
                break;
            }
        }
        if (room == null)
            throw new Exception("Room not found");

        if (room.Value.password != password)
            throw new Exception("Incorrect password");

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");

        var key = MakeKey(user_id, room.Value.room_id);
        var savedPos = ctx.Db.player_room_position.user_room_key.Find(key);

        var updatedPlayer = player;
        updatedPlayer.room_id = room.Value.room_id;
        updatedPlayer.last_position = savedPos?.last_position ?? new DbVector3(0, 0, 0);
        updatedPlayer.last_rotation = savedPos?.last_rotation ?? 0;
        updatedPlayer.last_room_join_time = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
        ctx.Db.online_player.user_id.Update(updatedPlayer);

        Log.Info($"User {user_id} joined room {room.Value.room_id}");
    }

    [Reducer]
    public static void LeaveRoom(ReducerContext ctx)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player is not in a room");

        // Save the player's position in the room
        var key = MakeKey(user_id, player.room_id);
        var savedPos = ctx.Db.player_room_position.user_room_key.Find(key);

        if (savedPos != null)
        {
            var updatedPos = savedPos.Value;
            updatedPos.last_position = player.last_position;
            updatedPos.last_rotation = player.last_rotation;
            updatedPos.last_updated = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.player_room_position.user_room_key.Update(updatedPos);
        }
        else
        {
            ctx.Db.player_room_position.Insert(new PlayerRoomPosition
            {
                user_id = user_id,
                room_id = player.room_id,
                last_position = player.last_position,
                last_rotation = player.last_rotation,
                user_room_key = key,
                last_updated = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }

        Log.Info($"User {user_id} left room {player.room_id}");

        var updatedPlayer = player;
        updatedPlayer.room_id = uint.MaxValue;
        updatedPlayer.last_position = new DbVector3(0, 0, 0);
        updatedPlayer.last_rotation = 0;
        ctx.Db.online_player.user_id.Update(updatedPlayer);
    }

    [Reducer]
    public static void SaveEntity(ReducerContext ctx, uint room_id, string data)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var room = ctx.Db.game_room.room_id.Find(room_id) ?? throw new Exception("Room not found");

        // Verify player is in the room
        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id != room_id)
            throw new Exception("Player must be in the room to save entities");

        // Check if the entity already exists
        var existingEntity = ctx.Db.room_entity.room_id.Find(room_id);
        if (existingEntity != null)
        {
            // Update the existing entity
            var updatedEntity = existingEntity.Value;
            updatedEntity.data = data;
            updatedEntity.user_id = user_id;
            updatedEntity.last_updated = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.room_entity.room_id.Update(updatedEntity);
        }
        else
        {
            // Insert a new entity
            ctx.Db.room_entity.Insert(new RoomEntity
            {
                room_id = room_id,
                user_id = user_id,
                data = data,
                last_updated = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }
    }

    public static string MakeKey(uint user_id, uint roomId) => $"{user_id}|{roomId}";
}