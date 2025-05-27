using SpacetimeDB;

public static partial class PlayerModule
{
    [Table(Name = "online_player", Public = true)]
    public partial struct OnlinePlayer
    {
        [PrimaryKey]
        public uint user_id;

        public Identity identity; // Keep for connection tracking
        public string username;
        public string color;

        [SpacetimeDB.Index.BTree]
        public uint room_id;
        public ulong last_room_join_time; // Time when the player joined the room

        public ulong last_connect_time; // Time when the player connected
        public ulong total_play_time; // Total time spent online

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
        public uint user_id;

        public string username;
        public string color;
        public ulong last_disconnect_time; // Time when the player left
        public ulong total_play_time; // Total time spent online
    }

    // Player-related reducers
    [Reducer]
    public static void SetPlayerProfile(ReducerContext ctx, string color)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        player.color = color;
        ctx.Db.online_player.user_id.Update(player);
    }

    [Reducer]
    public static void UpdateLastPosition(ReducerContext ctx, DbVector3 position, float rotation)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must join a room first");
        player.last_position = position;
        player.last_rotation = rotation;
        ctx.Db.online_player.user_id.Update(player);
    }

    public static string MakeKey(uint user_id, uint roomId) => $"{user_id}|{roomId}";

    // Helper method to get online player by user_id
    public static OnlinePlayer? GetOnlinePlayer(ReducerContext ctx, uint user_id)
    {
        return ctx.Db.online_player.user_id.Find(user_id);
    }

    // Helper method to get authenticated player
    public static OnlinePlayer GetAuthenticatedPlayer(ReducerContext ctx)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);
        return ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
    }
}