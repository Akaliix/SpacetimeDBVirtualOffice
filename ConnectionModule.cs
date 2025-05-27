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
        var count = ctx.Db.player_count.id.Find(0) ?? throw new Exception("Player count not initialized");

        var player = ctx.Db.logged_out_player.identity.Find(ctx.Sender);
        if (player != null)
        {
            ctx.Db.online_player.Insert(new PlayerModule.OnlinePlayer
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
            ctx.Db.online_player.Insert(new PlayerModule.OnlinePlayer
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
        ctx.Db.logged_out_player.Insert(new PlayerModule.LoggedOutPlayer
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
}