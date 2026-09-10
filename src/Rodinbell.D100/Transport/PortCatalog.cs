namespace Rodinbell.D100;

internal static class PortCatalog
{
    public static IReadOnlyList<PortDescriptor> Build(IEnumerable<string> currentPorts,
        IEnumerable<PortDescriptor> registryPorts, IEnumerable<string> presentDeviceIds)
    {
        var descriptors = currentPorts.ToDictionary(p => p, p => new PortDescriptor(p), StringComparer.OrdinalIgnoreCase);
        var present = presentDeviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidates in registryPorts
            .Where(p => p.DeviceId is not null && present.Contains(p.DeviceId) && descriptors.ContainsKey(p.PortName))
            .GroupBy(p => p.PortName, StringComparer.OrdinalIgnoreCase))
        {
            var identities = candidates.GroupBy(p => p.DeviceId!, StringComparer.OrdinalIgnoreCase).ToArray();
            if (identities.Length == 1)
                descriptors[candidates.Key] = identities[0].First();
        }
        return descriptors.Values.OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
