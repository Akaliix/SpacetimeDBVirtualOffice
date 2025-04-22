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

        public DbVector3 last_position;
    }

    [Table(Name = "player_room_position", Public = true)]
    public partial struct PlayerRoomPosition
    {
        [PrimaryKey]
        public string identity_room_key; // Example: "playerIdentity|roomId"

        public Identity identity;
        public uint room_id;
        public DbVector3 last_position;
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
        public uint room_id;
        public DbVector3 last_position;
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
        [PrimaryKey, AutoInc]
        public uint entity_id;
        [SpacetimeDB.Index.BTree]
        public uint room_id;
        public string prefab_id;
        public DbVector3 position;
        public DbVector3 rotation;
        public DbVector3 scale;
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
                last_position = player.Value.last_position
            });
            ctx.Db.logged_out_player.identity.Delete(player.Value.identity);
        }
        else
        {
            ctx.Db.online_player.Insert(new OnlinePlayer
            {
                identity = ctx.Sender,
                name = "",
                color = "#FFFFFF",
                room_id = uint.MaxValue,
                last_position = new DbVector3(0, 0, 0)
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
        ctx.Db.logged_out_player.Insert(new LoggedOutPlayer
        {
            identity = player.identity,
            player_id = player.player_id,
            name = player.name,
            color = player.color,
            room_id = player.room_id,
            last_position = player.last_position
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
            ctx.Db.player_room_position.identity_room_key.Update(savedPoss);
        }
        else
        {
            ctx.Db.player_room_position.Insert(new PlayerRoomPosition
            {
                identity = ctx.Sender,
                room_id = player.room_id,
                last_position = player.last_position,
                identity_room_key = key
            });
        }

        player.room_id = uint.MaxValue;
        player.last_position = new DbVector3(0, 0, 0);
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
    public static void UpdateLastPosition(ReducerContext ctx, DbVector3 position)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must join a room first");
        player.last_position = position;
        ctx.Db.online_player.identity.Update(player);

        // Save per-room position
        //var key = $"{ctx.Sender}|{player.room_id}";
        //var savedPos = ctx.Db.player_room_position.identity_room_key.Find(key);
        //if (savedPos != null)
        //{
        //    PlayerRoomPosition savedPoss = savedPos.Value;
        //    savedPoss.last_position = position;
        //    ctx.Db.player_room_position.identity_room_key.Update(savedPoss);
        //}
        //else
        //{
        //    ctx.Db.player_room_position.Insert(new PlayerRoomPosition
        //    {
        //        identity = ctx.Sender,
        //        room_id = player.room_id,
        //        last_position = position,
        //        identity_room_key = key
        //    });
        //}
    }
    [Reducer]
    public static void AddEntity(ReducerContext ctx, uint room_id, string prefab_id, DbVector3 pos, DbVector3 rot, DbVector3 scale)
    {
        ctx.Db.room_entity.Insert(new RoomEntity
        {
            room_id = room_id,
            prefab_id = prefab_id,
            position = pos,
            rotation = rot,
            scale = scale
        });
    }

    [Reducer]
    public static void RemoveEntity(ReducerContext ctx, uint entity_id)
    {
        ctx.Db.room_entity.entity_id.Delete(entity_id);
    }
}
