using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;

namespace PullTickSimConnectServer;


public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();

		Sim = new(this);

		Loaded += (s, e) => {
			Sim.Start();

			TcpStart();
		};
	}


	public object SyncRoot { get; init; } = new object();

	public RemotePacket RemotePacket;
	public AircraftPacket AircraftPacket;

	TcpListener TcpListener = new(IPAddress.Any, 25569);

	Sim Sim;

	void TcpStart() {
		new Thread(TcpLoop) {
			Name = "TCP server thread",
			IsBackground = true
		}.Start();
	}

	void HandleRemotePacket() {
		Sim.SendThrottle1Event(RemotePacket.throttle1);
		Sim.SendThrottle2Event(RemotePacket.throttle2);

		Sim.SendAileronsEvent((ushort) (0xFFFF - RemotePacket.ailerons));
		Sim.SendElevatorEvent(RemotePacket.elevator);
		Sim.SendRudderEvent((ushort) (0xFFFF - RemotePacket.rudder));

		Sim.SendFlapsEvent(RemotePacket.flaps);
		Sim.SendSpoilersEvent(RemotePacket.spoilers);
	}

	unsafe void HandleClient(TcpClient client) {
		new Thread(() => {
			Debug.WriteLine("[TCP] Got client");

			using var clientStream = client.GetStream();

			try {
				byte[] buffer;

				while (true) {
					//Debug.WriteLine("[TCP] Reading remote packet");

					buffer = new byte[sizeof(RemotePacket)];
					clientStream.ReadExactly(buffer, 0, buffer.Length);

					lock (SyncRoot) {
						RemotePacket = BytesToStruct<RemotePacket>(buffer);
					}

					//LogRemotePacket();
					HandleRemotePacket();

					// Writing
					//Debug.WriteLine("[TCP] Sending aircraft packet");

					buffer = new byte[sizeof(AircraftPacket)];

					lock (SyncRoot) {
						buffer = StructToBytes(AircraftPacket);
					}

					clientStream.Write(buffer, 0, buffer.Length);
				}

			}
			catch (Exception ex) {

			}
		}) {
			IsBackground = true
		}.Start();
	}

	void TcpLoop() {
		TcpListener.Start();

		while (true) {
			try {
				Debug.WriteLine("[TCP] Waiting for clients");

				var client = TcpListener.AcceptTcpClient();

				HandleClient(client);
			}
			catch (Exception ex) {

			}
		}
	}

	public static byte[] StructToBytes<T>(T str) where T : struct {
		int size = Marshal.SizeOf(str);

		byte[] arr = new byte[size];

		GCHandle h = default;

		try {
			h = GCHandle.Alloc(arr, GCHandleType.Pinned);

			Marshal.StructureToPtr<T>(str, h.AddrOfPinnedObject(), false);
		}
		finally {
			if (h.IsAllocated) {
				h.Free();
			}
		}

		return arr;
	}

	public static T BytesToStruct<T>(byte[] arr) where T : struct {
		T str = default;

		GCHandle h = default;

		try {
			h = GCHandle.Alloc(arr, GCHandleType.Pinned);

			str = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());

		}
		finally {
			if (h.IsAllocated) {
				h.Free();
			}
		}

		return str;
	}

	public void LogRemotePacket() {
		Debug.WriteLine("------------------ Remote packet ------------------");
		Debug.WriteLine($"throttle1: {RemotePacket.throttle1}");
		Debug.WriteLine($"throttle2: {RemotePacket.throttle2}");
		Debug.WriteLine($"ailerons: {RemotePacket.ailerons}");
		Debug.WriteLine($"elevator: {RemotePacket.elevator}");
		Debug.WriteLine($"rudder: {RemotePacket.rudder}");
		Debug.WriteLine($"flaps: {RemotePacket.flaps}");
		Debug.WriteLine($"spoilers: {RemotePacket.spoilers}");
		Debug.WriteLine($"landingGear: {RemotePacket.landingGear}");
		Debug.WriteLine($"strobeLights: {RemotePacket.strobeLights}");
	}

	public void LogAircraftPacket() {
		Debug.WriteLine("------------------ Aircraft packet ------------------");
		Debug.WriteLine($"Latitude: {AircraftPacket.latitude}");
		Debug.WriteLine($"Longitude: {AircraftPacket.longitude}");
		Debug.WriteLine($"Pitch: {AircraftPacket.pitch}");
		Debug.WriteLine($"Yaw: {AircraftPacket.yaw}");
		Debug.WriteLine($"Roll: {AircraftPacket.roll}");
		Debug.WriteLine($"Altitude: {AircraftPacket.altitude}");
		Debug.WriteLine($"Speed: {AircraftPacket.speed}");
		Debug.WriteLine($"Pressure: {AircraftPacket.pressure}");
		Debug.WriteLine($"Temperature: {AircraftPacket.temperature}");
	}
}