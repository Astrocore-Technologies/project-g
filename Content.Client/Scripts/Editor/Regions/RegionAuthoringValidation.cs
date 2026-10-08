using Godot;

namespace ProjectG.Regions.Authoring;

/// <summary>Editor/preview checks. Export geometry and server-rule validation are added in R2.</summary>
public static class RegionAuthoringValidation
{
    public static List<string> Validate(RegionRoot root)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(root.RegionId) || root.RegionId.Length > 48 ||
            root.RegionId.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
            errors.Add("RegionId must contain 1–48 lowercase ASCII letters, digits, '_' or '-'.");
        if (!root.Transform.IsEqualApprox(Transform3D.Identity))
            errors.Add("RegionRoot must have identity transform.");
        var bounds = root.MovementBounds;
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || bounds.Size.X <= 0 || bounds.Size.Y <= 0)
            errors.Add("MovementBounds must be finite and positive.");

        foreach (var name in new[] { "Environment", "Terrain", "Decorations", "PublicStateBindings", "AuthoringAnchors" })
            if (root.GetNodeOrNull<Node3D>(name) is null)
                errors.Add($"Missing Node3D child: {name}.");

        var ids = new HashSet<Guid>();
        Visit(root, root, Transform3D.Identity, ids, errors);
        return errors;
    }

    private static void Visit(RegionRoot root, Node parent, Transform3D parentTransform,
        HashSet<Guid> ids, List<string> errors)
    {
        foreach (var child in parent.GetChildren())
        {
            var transform = child is Node3D spatial ? parentTransform * spatial.Transform : parentTransform;
            if (child is RegionRoot)
                errors.Add($"{root.GetPathTo(child)}: nested RegionRoot is not supported.");
            if (child is RegionMarker marker)
            {
                var path = root.GetPathTo(marker).ToString();
                if (!Guid.TryParseExact(marker.AuthoredObjectId, "D", out var id) || id == Guid.Empty)
                    errors.Add($"{path}: invalid non-empty UUID.");
                else if (!ids.Add(id))
                    errors.Add($"{path}: duplicate AuthoredObjectId {id}.");
                var anchors = root.GetNodeOrNull<Node3D>("AuthoringAnchors");
                if (anchors is null || !anchors.IsAncestorOf(marker))
                    errors.Add($"{path}: marker must be under AuthoringAnchors.");
                if (!transform.Origin.IsFinite() || !transform.Basis.IsFinite() ||
                    !transform.Basis.IsEqualApprox(Basis.Identity) || !Mathf.IsZeroApprox(transform.Origin.Y))
                    errors.Add($"{path}: demo markers need finite X/Z placement, Y=0 and identity basis.");
                if (!root.MovementBounds.HasPoint(new Vector2(transform.Origin.X, transform.Origin.Z)))
                    errors.Add($"{path}: outside MovementBounds.");
                if (marker is GateMarker gate && (!float.IsFinite(gate.Radius) || gate.Radius <= 0 || gate.Radius > 10))
                    errors.Add($"{path}: gate radius must be in (0, 10].");
                if (marker is NavigationPatchMarker patch &&
                    (!patch.Footprint.IsFinite() || patch.Footprint.X <= 0 || patch.Footprint.Y <= 0))
                    errors.Add($"{path}: patch footprint must be finite and positive.");
            }
            Visit(root, child, transform, ids, errors);
        }
    }
}
