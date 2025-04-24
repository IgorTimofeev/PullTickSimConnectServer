using System.Runtime.InteropServices;

namespace PullTickSimConnectServer
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct RemotePacket {
		public ushort throttle1;
		public ushort throttle2;

		public ushort ailerons;
		public ushort elevator;
		public ushort rudder;

		public ushort flaps;
		public ushort spoilers;

		public uint altimeterPressurePa;

		public ushort autopilotAirspeedMs;
		public bool autopilotAutoThrottle;

		public ushort autopilotHeadingDeg;
		public bool autopilotHeadingHold;

		public ushort autopilotAltitudeM;
		public bool autopilotLevelChange;

		public bool landingGear;
		public bool strobeLights;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct AircraftPacket {
		public float latitudeRad;
		public float longitudeRad;
		public float altitudeM;

		public float pitchRad;
		public float yawRad;
		public float rollRad;

		public float airSpeedMs;
		public float groundSpeedMs;

		public float flightPathPitch;
		public float flightPathYaw;

		public ushort slipAndSkid;

		public ushort windDirectionDeg;
		public float windSpeedMs;
	}
}
