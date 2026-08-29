namespace RoutePacer.App.Rides;

/// <summary>
/// What kind of pause a <see cref="RideSessionState.Paused"/> ride is in. The first three keep the
/// GPS watch up so movement can end the pause; <see cref="Suspended"/> has given the watch back to
/// save battery, and only a tap brings it out.
/// </summary>
public enum PauseMode { None, AutoStationary, Manual, Suspended }
