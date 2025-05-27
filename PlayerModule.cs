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
    public static void SetPlayerProfile(ReducerContext ctx, string username, string color)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Validate input
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 50)
            throw new Exception("Username must be between 3 and 50 characters");

        if (string.IsNullOrWhiteSpace(color) || color.Length > 10)
            throw new Exception("Color must be a valid color string (max 10 characters)");

        // No need to check for username uniqueness since usernames are not unique anymore

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        var userAccount = AuthModule.GetUserAccount(ctx, user_id) ?? throw new Exception("User account not found");

        // Check if username is actually changing
        bool usernameChanged = userAccount.username != username;

        // Update user account username
        userAccount.username = username;
        ctx.Db.user_account.user_id.Update(userAccount);

        // Update online player
        player.username = username;
        player.color = color;
        ctx.Db.online_player.user_id.Update(player);

        // Update logged out player data if it exists (for future reference)
        var loggedOutPlayer = ctx.Db.logged_out_player.user_id.Find(user_id);
        if (loggedOutPlayer != null)
        {
            var updatedLoggedOut = loggedOutPlayer.Value;
            updatedLoggedOut.username = username;
            updatedLoggedOut.color = color;
            ctx.Db.logged_out_player.user_id.Update(updatedLoggedOut);
        }

        // Update communication records if username changed
        if (usernameChanged)
        {
            CommunicationModule.UpdateCommunicationUsername(ctx, user_id, username);
        }

        Log.Info($"Player profile updated: User {user_id} changed to username '{username}' and color '{color}'");
    }

    [Reducer]
    public static void SetPlayerColor(ReducerContext ctx, string color)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Validate input
        if (string.IsNullOrWhiteSpace(color) || color.Length > 10)
            throw new Exception("Color must be a valid color string (max 10 characters)");

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");

        // Update online player color only
        player.color = color;
        ctx.Db.online_player.user_id.Update(player);

        // Update logged out player data if it exists (for future reference)
        var loggedOutPlayer = ctx.Db.logged_out_player.user_id.Find(user_id);
        if (loggedOutPlayer != null)
        {
            var updatedLoggedOut = loggedOutPlayer.Value;
            updatedLoggedOut.color = color;
            ctx.Db.logged_out_player.user_id.Update(updatedLoggedOut);
        }

        Log.Info($"Player color updated: User {user_id} changed color to '{color}'");
    }

    [Reducer]
    public static void SetPlayerUsername(ReducerContext ctx, string username)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Validate input
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 50)
            throw new Exception("Username must be between 3 and 50 characters");

        // No need to check for username uniqueness since usernames are not unique anymore

        var player = ctx.Db.online_player.user_id.Find(user_id) ?? throw new Exception("Player not found");
        var userAccount = AuthModule.GetUserAccount(ctx, user_id) ?? throw new Exception("User account not found");

        // Update user account username
        userAccount.username = username;
        ctx.Db.user_account.user_id.Update(userAccount);

        // Update online player username
        player.username = username;
        ctx.Db.online_player.user_id.Update(player);

        // Update logged out player data if it exists (for future reference)
        var loggedOutPlayer = ctx.Db.logged_out_player.user_id.Find(user_id);
        if (loggedOutPlayer != null)
        {
            var updatedLoggedOut = loggedOutPlayer.Value;
            updatedLoggedOut.username = username;
            ctx.Db.logged_out_player.user_id.Update(updatedLoggedOut);
        }

        // Update communication records with new username
        CommunicationModule.UpdateCommunicationUsername(ctx, user_id, username);

        Log.Info($"Player username updated: User {user_id} changed username to '{username}'");
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