using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Sockets.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
       public HubConnection _connection;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void bConnect_Click(object sender, RoutedEventArgs e)
        {
            var baseUrl = "http://localhost:1850/hubs";
            using (var httpClient = new HttpClient())
            {
                var transport = new LongPollingTransport(httpClient);
                var connection = new HubConnection(new Uri(baseUrl));
                try
                {
                    await _connection.StartAsync(transport, httpClient);



                    // Set up handler
                    _connection.On("Send", new[] { typeof(string) }, a =>
                    {
                        var message = (string)a[0];
                        //Add message to list view if designer view would ever load.
                    });


                }
                finally
                {
                    await connection.DisposeAsync();
                }
            }
        }

        private async void bSend_Click(object sender, RoutedEventArgs e)
        {
            var line = tbMessage.Text;
            await _connection.Invoke<object>("Send", line);

        }
    }
    
}
