using System.Runtime.InteropServices;

namespace PullTickSimConnectServer
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct RemotePacket {
		public RemotePacket() {

		}

		public byte Throttle = 0;

		public byte Ailerons = 0;
		public byte Elevator = 0;
		public byte Rudder = 0;

		public byte Flaps = 0;
		public byte Spoilers = 0;

		public uint AltimeterPressurePa = 101325;

		public ushort AutopilotAirspeedMs = 0;
		public bool AutopilotAutoThrottle = false;

		public ushort AutopilotHeadingDeg = 0;
		public bool AutopilotHeadingHold = false;

		public ushort AutopilotAltitudeM = 0;
		public bool AutopilotLevelChange = false;

		public bool LandingGear = false;
		public bool StrobeLights = false;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct AircraftPacket {
		public byte Throttle;

		public float LatitudeRad;
		public float LongitudeRad;
		public float AltitudeM;

		public float PitchRad;
		public float YawRad;
		public float RollRad;

		public float AirSpeedMs;
		public float GroundSpeedMs;

		public float FlightPathPitch;
		public float FlightPathYaw;

		public float FlightDirectorPitch;
		public float FlightDirectorYaw;

		public ushort SlipAndSkid;

		public ushort SindDirectionDeg;
		public float WindSpeedMs;
	}
}
