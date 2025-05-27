using SpacetimeDB;

public static partial class AuthModule
{
    [Table(Name = "user_account", Public = false)]
    public partial struct UserAccount
    {
        [PrimaryKey, AutoInc]
        public uint user_id;

        [Unique]
        public string email;

        public string username; // Display name, not unique
        public string password_hash;
        public string salt;
        public ulong created_at;
        public ulong last_login;
    }

    [Table(Name = "user_session", Public = false)]
    public partial struct UserSession
    {
        [PrimaryKey]
        public Identity identity;

        public uint user_id;
        public ulong created_at;
        public ulong last_activity;
    }

    [Reducer]
    public static void Register(ReducerContext ctx, string email, string username, string password)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(email) || email.Length < 5 || email.Length > 100 || !email.Contains("@"))
            throw new Exception("Email must be a valid email address");

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 50)
            throw new Exception("Username must be between 3 and 50 characters");

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new Exception("Password must be at least 6 characters");

        // Check if email already exists
        var existingUser = ctx.Db.user_account.email.Find(email);
        if (existingUser != null)
            throw new Exception("Email already exists");

        // Generate salt and hash password
        string salt = GenerateSalt(ctx);
        string passwordHash = HashPassword(password, salt);

        // Create user account
        var newUser = ctx.Db.user_account.Insert(new UserAccount
        {
            email = email,
            username = username,
            password_hash = passwordHash,
            salt = salt,
            created_at = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
            last_login = 0
        });

        Log.Info($"User registered: {email} with username {username} and ID: {newUser.user_id}");
    }

    [Reducer]
    public static void Login(ReducerContext ctx, string email, string password)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new Exception("Email and password are required");

        // Find user account
        var userAccount = ctx.Db.user_account.email.Find(email);
        if (userAccount == null)
            throw new Exception("Invalid email or password");

        // Verify password
        string passwordHash = HashPassword(password, userAccount.Value.salt);
        if (passwordHash != userAccount.Value.password_hash)
            throw new Exception("Invalid email or password");

        // Update last login time
        var updatedUser = userAccount.Value;
        updatedUser.last_login = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
        ctx.Db.user_account.user_id.Update(updatedUser);

        // Create or update session
        var existingSession = ctx.Db.user_session.identity.Find(ctx.Sender);
        if (existingSession != null)
        {
            var updatedSession = existingSession.Value;
            updatedSession.user_id = userAccount.Value.user_id;
            updatedSession.last_activity = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            ctx.Db.user_session.identity.Update(updatedSession);
        }
        else
        {
            ctx.Db.user_session.Insert(new UserSession
            {
                identity = ctx.Sender,
                user_id = userAccount.Value.user_id,
                created_at = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch,
                last_activity = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch
            });
        }

        Log.Info($"User logged in: {email} (ID: {userAccount.Value.user_id})");
    }

    [Reducer]
    public static void Logout(ReducerContext ctx)
    {
        var session = ctx.Db.user_session.identity.Find(ctx.Sender);
        if (session != null)
        {
            ctx.Db.user_session.identity.Delete(ctx.Sender);
            Log.Info($"User logged out: {session.Value.user_id}");
        }
    }

    // Helper method to get authenticated user ID
    public static uint GetAuthenticatedUserId(ReducerContext ctx)
    {
        var session = ctx.Db.user_session.identity.Find(ctx.Sender);
        if (session == null)
            throw new Exception("Authentication required. Please login first.");

        // Update last activity
        var updatedSession = session.Value;
        updatedSession.last_activity = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
        ctx.Db.user_session.identity.Update(updatedSession);

        return session.Value.user_id;
    }

    // Helper method to check if user is authenticated
    public static bool IsAuthenticated(ReducerContext ctx)
    {
        return ctx.Db.user_session.identity.Find(ctx.Sender) != null;
    }

    // Helper method to get user account by user_id
    public static UserAccount? GetUserAccount(ReducerContext ctx, uint user_id)
    {
        return ctx.Db.user_account.user_id.Find(user_id);
    }

    private static string GenerateSalt(ReducerContext ctx)
    {
        byte[] saltBytes = new byte[32];
        ctx.Rng.NextBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    private static string HashPassword(string password, string salt)
    {
        var hash = new HashCode();
        hash.Add(password);
        hash.Add(salt);
        return hash.ToHashCode().ToString("X");
    }
}