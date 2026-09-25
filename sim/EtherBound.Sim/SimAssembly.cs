namespace EtherBound.Sim;

/// <summary>Anchor for reflection over the sim assembly (architecture tests, the host's wiring).</summary>
public static class SimAssembly
{
    public static System.Reflection.Assembly Assembly => typeof(SimAssembly).Assembly;
}
