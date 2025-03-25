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

		public bool landingGear;
		public bool strobeLights;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct AircraftPacket {
		public float latitude;
		public float longitude;
		public float altitude;

		public float x;
		public float y;
		public float z;

		public float pitch;
		public float yaw;
		public float roll;

		public float speed;

		public float flightPathPitch;
		public float flightPathYaw;

		public float slipAndSkid;

		public float pressure;
		public float temperature;
	}
}
