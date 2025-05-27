using SpacetimeDB;

public static partial class RoomModule
{
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

    [Table(Name = "player_room_position")]
    public partial struct PlayerRoomPosition
    {
        [PrimaryKey]
        public string identity_room_key; // Example: "playerIdentity|roomId"

        public Identity identity;
        public uint room_id;
        public DbVector3 last_position;
        public float last_rotation;
    }

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
        //var key = $"{ctx.Sender}|{room_id}";
        var key = MakeKey(ctx.Sender, room_id);
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
        //var key = $"{ctx.Sender}|{player.room_id}";
        var key = MakeKey(ctx.Sender, player.room_id);
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

    public static string MakeKey(Identity identity, uint roomId) => $"{identity.ToString()}|{roomId}";
}