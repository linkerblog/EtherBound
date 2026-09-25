namespace EtherBound.Host;

/// <summary>Anchor for reflection over the host assembly (architecture tests).</summary>
public static class HostAssembly
{
    public static System.Reflection.Assembly Assembly => typeof(HostAssembly).Assembly;
}
