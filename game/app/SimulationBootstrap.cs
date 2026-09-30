using System.Threading.Tasks;
using EtherBound.Sim.Engine;
using Godot;

namespace EtherBound.Game.App;

public partial class SimulationBootstrap : Node
{
    public Task<WorldEngineCatalogs> Catalogs { get; private set; } = null!;

    public override void _EnterTree()
    {
        base._EnterTree();
        Catalogs = Task.Run(WorldEngine.LoadCatalogs);
    }
}
