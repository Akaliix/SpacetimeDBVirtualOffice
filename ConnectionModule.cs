using SpacetimeDB;

public static partial class ConnectionModule
{
    [Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info("Server initialized.");
        ctx.Db.player_count.Insert(new PlayerModule.PlayerCount { id = 0, count = 0 });
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void Connect(ReducerContext ctx)
    {
        // Don't automatically create players on connection
        // Players must login first
        Log.Info($"Client connected: {ctx.Sender}");
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void Disconnect(ReducerContext ctx)
    {
        var count = ctx.Db.player_count.id.Find(0) ?? throw new Exception("Player count not initialized");

        // Check if this identity had an active session
        var session = ctx.Db.user_session.identity.Find(ctx.Sender);
        if (session != null)
        {
            uint user_id = session.Value.user_id;

            // Find the online player
            var player = ctx.Db.online_player.user_id.Find(user_id);
            if (player != null)
            {
                ulong currentTime = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;

                // Move to logged out players table
                var existingLoggedOut = ctx.Db.logged_out_player.user_id.Find(user_id);
                if (existingLoggedOut != null)
                {
                    var updatedLoggedOut = existingLoggedOut.Value;
                    updatedLoggedOut.last_disconnect_time = currentTime;
                    updatedLoggedOut.total_play_time += (currentTime - player.Value.last_connect_time);
                    ctx.Db.logged_out_player.user_id.Update(updatedLoggedOut);
                }
                else
                {
                    ctx.Db.logged_out_player.Insert(new PlayerModule.LoggedOutPlayer
                    {
                        user_id = user_id,
                        username = player.Value.username,
                        color = player.Value.color,
                        last_disconnect_time = currentTime,
                        total_play_time = player.Value.total_play_time + (currentTime - player.Value.last_connect_time)
                    });
                }

                // Remove from online players
                ctx.Db.online_player.user_id.Delete(user_id);

                count.count -= 1;
                ctx.Db.player_count.id.Update(count);

                Log.Info($"Player disconnected: {player.Value.username} (ID: {user_id})");
            }

            // Remove the session
            ctx.Db.user_session.identity.Delete(ctx.Sender);
        }
    }

    // This reducer is called after successful login to create the online player
    [Reducer]
    public static void CreateOnlinePlayer(ReducerContext ctx)
    {
        uint user_id = AuthModule.GetAuthenticatedUserId(ctx);

        // Check if player is already online
        var existingPlayer = ctx.Db.online_player.user_id.Find(user_id);
        if (existingPlayer != null)
            throw new Exception("Player is already online");

        // Get user account info
        var userAccount = AuthModule.GetUserAccount(ctx, user_id) ?? throw new Exception("User account not found");

        var count = ctx.Db.player_count.id.Find(0) ?? throw new Exception("Player count not initialized");

        // Check if there's saved data from previous logout
        var loggedOutPlayer = ctx.Db.logged_out_player.user_id.Find(user_id);

        string color = "#FFFFFF";
        ulong totalPlayTime = 0;

        if (loggedOutPlayer != null)
        {
            color = loggedOutPlayer.Value.color;
            totalPlayTime = loggedOutPlayer.Value.total_play_time;
            ctx.Db.logged_out_player.user_id.Delete(user_id);
        }

        // Create online player
        ctx.Db.online_player.Insert(new PlayerModule.OnlinePlayer
        {
            user_id = user_id,
            identity = ctx.Sender,
            username = userAccount.username,
            color = color,
            room_id = uint.MaxValue,
            last_position = new DbVector3(0, 0, 0),
            last_rotation = 0,
            last_connect_time = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
            total_play_time = totalPlayTime
        });

        count.count += 1;
        ctx.Db.player_count.id.Update(count);

        Log.Info($"Online player created: {userAccount.username} (ID: {user_id})");
    }
}