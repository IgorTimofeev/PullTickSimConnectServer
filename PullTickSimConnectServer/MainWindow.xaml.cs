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
		Tcp = new(this);

		Loaded += (s, e) => {
			Sim.Start();
			Tcp.Start();
		};
	}


	public object PacketsSyncRoot { get; init; } = new object();

	public RemotePacket RemotePacket;
	public AircraftPacket AircraftPacket;

	Sim Sim;
	Tcp Tcp;

	public void HandleRemotePacket() {
		Sim.SendThrottle1Event(RemotePacket.throttle1);
		Sim.SendThrottle2Event(RemotePacket.throttle2);

		Sim.SendAileronsEvent((ushort) (0xFFFF - RemotePacket.ailerons));
		Sim.SendElevatorEvent(RemotePacket.elevator);
		Sim.SendRudderEvent((ushort) (0xFFFF - RemotePacket.rudder));

		Sim.SendFlapsEvent(RemotePacket.flaps);
		Sim.SendSpoilersEvent(RemotePacket.spoilers);
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