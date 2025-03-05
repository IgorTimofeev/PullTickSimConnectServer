using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PullTickSimConnectServer;

public partial class MainWindow : Window {
	public MainWindow() {
		InitializeComponent();

		Sim = new(this);
		Tcp = new(this);

		PortTextBoxInputTimer = new(
			TimeSpan.FromSeconds(1),
			DispatcherPriority.ApplicationIdle,
			(s, e) => {
				PortTextBoxInputTimer!.Stop();

				TCPReconnect();
			},
			Dispatcher
		);

		PortTextBoxInputTimer.Stop();

		Loaded += (s, e) => {
			//Sim.Start();
			TCPReconnect();
		};
	}

	public object PacketsSyncRoot { get; init; } = new object();

	public RemotePacket RemotePacket;
	public AircraftPacket AircraftPacket;

	public static unsafe int RemotePacketSize => sizeof(RemotePacket);
	public static unsafe int AircraftPacketSize => sizeof(AircraftPacket);

	Sim Sim;
	TCP Tcp;
	DispatcherTimer PortTextBoxInputTimer;

	public void HandleRemotePacket() {
		if (!Sim.IsConnected)
			return;

		Sim.SendThrottle1Event(RemotePacket.throttle1);
		Sim.SendThrottle2Event(RemotePacket.throttle2);

		Sim.SendAileronsEvent((ushort) (0xFFFF - RemotePacket.ailerons));
		Sim.SendElevatorEvent(RemotePacket.elevator);
		Sim.SendRudderEvent((ushort) (0xFFFF - RemotePacket.rudder));

		Sim.SendFlapsEvent(RemotePacket.flaps);
		Sim.SendSpoilersEvent(RemotePacket.spoilers);
	}

	void OnWindowCloseButtonClick(object sender, RoutedEventArgs e) {
		Close();
	}

	void OnWindowMinimizeButtonClick(object sender, RoutedEventArgs e) {
		WindowState = WindowState.Minimized;
	}

	void OnTopPanelMouseDown(object sender, MouseButtonEventArgs e) {
		DragMove();
	}

	void UpdateSyncEllipseColor() {
		//StatusIndicator.Fill = (SolidColorBrush) App.Current.Resources["ThemeGood2"];
	}

	void TCPReconnect() {
		if (!int.TryParse(PortTextBox.Text, out var port))
			return;

		if (Tcp.IsStarted)
			Tcp.Stop();

		Tcp.Start(port);
	}

	void OnPortTextBoxKeyUp(object sender, KeyEventArgs e) {
		if (e.Key is not Key.Enter)
			return;

		FocusManager.SetFocusedElement(FocusManager.GetFocusScope(PortTextBox), null);
		Keyboard.ClearFocus();
	}

	void CheckPortForRetardness() {
		PortTitle.Text =
			int.TryParse(PortTextBox.Text, out _)
			? "Server port"
			: "Retarted port";
	}

	void OnPortTextBoxLostFocus(object sender, RoutedEventArgs e) {
		CheckPortForRetardness();

		PortTextBoxInputTimer.Start();
	}
}