namespace EtherBound.Llm.Bench;

/// <summary>
/// The fixed corpus of Dev-007 [Sec. 4]: 40 sentences against the candidates `--list` prints (Niko beside the
/// test world's shovel and chest). `Expected` is the label of the one candidate the sentence asks for, or
/// null when nothing listed does. Ten sentences are Spanish, ten ask for something that is not there.
/// </summary>
public static class Corpus
{
    public sealed record Line(string Text, string? Expected);

    private const string Wait = "wait (here)";
    private const string TakeShovel = "take: Shovel (east)";
    private const string InspectShovel = "inspect: Shovel (east)";
    private const string InspectChest = "inspect: Chest (south-east)";

    public static readonly IReadOnlyList<Line> All = new Line[]
    {
        new("wait", Wait),
        new("let some time pass", Wait),
        new("rest for a moment", Wait),
        new("espera un momento", Wait),
        new("take the shovel", TakeShovel),
        new("pick up the shovel", TakeShovel),
        new("grab the shovel next to me", TakeShovel),
        new("coge la pala", TakeShovel),
        new("toma la pala que esta al este", TakeShovel),
        new("examine the shovel", InspectShovel),
        new("look at the shovel", InspectShovel),
        new("mira la pala", InspectShovel),
        new("inspect the chest", InspectChest),
        new("look at the chest to the southeast", InspectChest),
        new("check what the chest looks like", InspectChest),
        new("hit the shovel", "hit: Shovel (east)"),
        new("smack the shovel with your fist", "hit: Shovel (east)"),
        new("push the shovel", "push: Shovel east (east)"),
        new("shove the shovel away from you", "push: Shovel east (east)"),
        new("pull the shovel toward me", "pull: Shovel west (east)"),
        new("drag the shovel behind you", "drag: Shovel west (east)"),
        new("break the shovel", "break: Shovel (east)"),
        new("climb the grass to the east", "climb: Grass (east)"),
        new("sube hacia el este", "climb: Grass (east)"),
        new("inspect the asphalt to the west", "inspect: Asphalt (west)"),
        new("look at the ground here", "inspect: Asphalt (here)"),
        new("look north", "inspect: Asphalt (north)"),
        new("mira al sur", "inspect: Asphalt (south)"),
        new("examine the grass to the northeast", "inspect: Grass (north-east)"),
        new("dig up the grass to the east", "dig: Grass (east)"),
        new("sing a song", null),
        new("fly away", null),
        new("talk to the shopkeeper", null),
        new("eat an apple", null),
        new("order a pizza", null),
        new("baila un poco", null),
        new("call my mother", null),
        new("cast an Ether blast at the sky", null),
        new("buy the shovel", null),
        new("go to sleep", null),
    };
}
