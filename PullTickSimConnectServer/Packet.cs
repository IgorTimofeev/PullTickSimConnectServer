using System.Runtime.InteropServices;
using System.Text;

namespace PullTickSimConnectServer
{
	public class Packet {
		public const uint Header = 0xAABBCCDD;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct AircraftPacket {
		public AircraftPacket() {

		}

		public uint Header = 0;

		public float Throttle = 0;
		public float Ailerons = 0;
		public float Elevator = 0;
		public float Rudder = 0;
		public float Flaps = 0;
		public byte Lights = 0;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct SimPacket {
		public SimPacket() {

		}

		public uint Header = 0;

		public float AccelerationX = 0;
		
		public float RollRad = 0;
		public float PitchRad = 0;
		public float YawRad = 0;

		public float LatitudeRad = 0;
		public float LongitudeRad = 0;

		public float SpeedMPS = 0;

		public float PressurePA = 0;
		public float TemperatureC = 0;
	}
}
