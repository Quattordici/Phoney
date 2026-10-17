using System.Runtime.CompilerServices;

// Stack buffers (templates, names, emails) are always written before they are read, so the runtime need not zero them on every call.
// A buffer that must start cleared clears itself (see SubsetPicker.DrawWeightedPositions).
[module: SkipLocalsInit]
