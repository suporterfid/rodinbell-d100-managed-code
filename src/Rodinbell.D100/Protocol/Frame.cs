namespace Rodinbell.D100;

internal sealed record Frame(byte Address, byte Command, byte[] Payload);
