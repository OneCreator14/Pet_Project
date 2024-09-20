using AngleSharp.Io;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace VK_Parser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Parser _Parser;
        internal static MainWindow main;

        internal string Log
        {
            get { return textBlockLog.Text; }
            set { Dispatcher.Invoke(new Action(() => { textBlockLog.Text += value; })); }
        }

        public MainWindow()
        {
            //InitializeComponent();
            _Parser = new Parser();
            InitializeComponent();
            main = this;

            textBoxURL.DataContext = _Parser;
            textBoxPath.DataContext = _Parser;
            textBoxFileSize.DataContext = _Parser;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (_Parser.CheckValues())
            {
                Log = "Программа запущена\n";
                _Parser.Parse();
            }
            else
            {
                MessageBox.Show("Заполните все поля");
            }
        }

        private void withVideo_Checked(object sender, RoutedEventArgs e)
        {
            _Parser.onlyAudio = false;
        }

        private void onlyAudio_Checked(object sender, RoutedEventArgs e)
        {
            _Parser.onlyAudio = true;
        }

        private void downloadFromComments_Clicked(object sender, RoutedEventArgs e)
        {
            _Parser.downloadFromComments = !(_Parser.downloadFromComments);
        }
    }
}