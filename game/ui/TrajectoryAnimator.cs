using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

public partial class TrajectoryAnimator : Node3D
{
    private sealed class Trail
    {
        public required MeshInstance3D Marker { get; init; }
        public required Vector3[] Points { get; init; }
        public required StandardMaterial3D Material { get; init; }
        public double Elapsed { get; set; }
    }

    private readonly List<Trail> _trails = new();

    public void Play(IEnumerable<HostTrajectoryPoint> trajectory)
    {
        foreach (var points in trajectory.GroupBy(p => (p.Kind, p.ActorId, p.ObjectId)).Select(g => g.ToArray()))
        {
            if (points.Length < 2) continue;
            var color = points[0].Kind == "object" ? new Color("#ff6ac1") : new Color("#57c7ff");
            var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color };
            var marker = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 3 },
                MaterialOverride = material,
            };
            AddChild(marker);
            var positions = points.Select(p => new Vector3((float)p.X, p.H * 0.5f, (float)p.Y)).ToArray();
            marker.Position = positions[0];
            _trails.Add(new Trail { Marker = marker, Points = positions, Material = material });
        }
    }

    public void Advance(double deltaSeconds)
    {
        for (var i = _trails.Count - 1; i >= 0; i--)
        {
            var trail = _trails[i];
            trail.Elapsed += deltaSeconds;
            var progress = Mathf.Clamp((float)(trail.Elapsed / 0.65), 0, 1);
            var position = progress * (trail.Points.Length - 1);
            var segment = Math.Min((int)position, trail.Points.Length - 2);
            trail.Marker.Position = trail.Points[segment].Lerp(trail.Points[segment + 1], position - segment);
            var color = trail.Material.AlbedoColor;
            color.A = 1 - progress;
            trail.Material.AlbedoColor = color;
            if (progress < 1) continue;
            trail.Marker.QueueFree();
            _trails.RemoveAt(i);
        }
    }
}
