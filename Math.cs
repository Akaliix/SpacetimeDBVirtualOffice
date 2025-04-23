[SpacetimeDB.Type]
public partial struct DbVector2
{
    public float x;
    public float y;

    public DbVector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public float SqrMagnitude => x * x + y * y;

    public float Magnitude => MathF.Sqrt(SqrMagnitude);

    public DbVector2 Normalized => this / Magnitude;

    public static DbVector2 operator +(DbVector2 a, DbVector2 b) => new DbVector2(a.x + b.x, a.y + b.y);
    public static DbVector2 operator -(DbVector2 a, DbVector2 b) => new DbVector2(a.x - b.x, a.y - b.y);
    public static DbVector2 operator *(DbVector2 a, float b) => new DbVector2(a.x * b, a.y * b);
    public static DbVector2 operator /(DbVector2 a, float b) => new DbVector2(a.x / b, a.y / b);
}

[SpacetimeDB.Type]
public partial struct DbVector3
{
    public float x;
    public float y;
    public float z;
    public DbVector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public float SqrMagnitude => x * x + y * y + z * z;
    public float Magnitude => MathF.Sqrt(SqrMagnitude);
    public DbVector3 Normalized => this / Magnitude;
    public static DbVector3 operator +(DbVector3 a, DbVector3 b) => new DbVector3(a.x + b.x, a.y + b.y, a.z + b.z);
    public static DbVector3 operator -(DbVector3 a, DbVector3 b) => new DbVector3(a.x - b.x, a.y - b.y, a.z - b.z);
    public static DbVector3 operator *(DbVector3 a, float b) => new DbVector3(a.x * b, a.y * b, a.z * b);
    public static DbVector3 operator /(DbVector3 a, float b) => new DbVector3(a.x / b, a.y / b, a.z / b);
}

[SpacetimeDB.Type]
public partial struct DbQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;
    public DbQuaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
    public static DbQuaternion operator *(DbQuaternion a, DbQuaternion b)
    {
        return new DbQuaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
    }
}