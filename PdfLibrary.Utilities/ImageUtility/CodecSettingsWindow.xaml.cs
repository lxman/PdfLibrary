using System.Windows;
using System.Windows.Controls;
using ImageUtility.Codecs;

namespace ImageUtility;

public partial class CodecSettingsWindow : Window
{
    private readonly CodecConfiguration _configuration;
    private readonly Dictionary<string, ComboBox> _decoderComboBoxes = new();
    private readonly Dictionary<string, ComboBox> _encoderComboBoxes = new();

    public CodecSettingsWindow()
    {
        InitializeComponent();

        // Load current configuration
        _configuration = CodecRegistry.Instance.Configuration;

        // Build the UI dynamically based on available codecs
        BuildSettingsUI();
    }

    private void BuildSettingsUI()
    {
        // Get all unique extensions from registered codecs
        var allExtensions = CodecRegistry.Instance.GetAllCodecs()
            .SelectMany(c => c.Extensions)
            .Distinct()
            .OrderBy(ext => ext)
            .ToList();

        foreach (string extension in allExtensions)
        {
            // Get codecs that can handle this extension
            var decoders = CodecRegistry.Instance.GetAllCodecs()
                .Where(c => c.Extensions.Contains(extension) && c.CanDecode)
                .ToList();

            var encoders = CodecRegistry.Instance.GetAllCodecs()
                .Where(c => c.Extensions.Contains(extension) && c.CanEncode)
                .ToList();

            // Skip if no codecs available for this extension
            if (decoders.Count == 0 && encoders.Count == 0)
            {
                continue;
            }

            // Create a group box for this format
            var groupBox = new GroupBox
            {
                Header = $"{extension.ToUpperInvariant()} Format",
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Decoder row
            if (decoders.Count > 0)
            {
                var decoderLabel = new TextBlock
                {
                    Text = "Decoder:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 10, 5)
                };
                Grid.SetRow(decoderLabel, 0);
                Grid.SetColumn(decoderLabel, 0);
                grid.Children.Add(decoderLabel);

                var decoderCombo = new ComboBox
                {
                    Margin = new Thickness(0, 5, 0, 5)
                };

                // Add "Auto" option
                decoderCombo.Items.Add(new ComboBoxItem
                {
                    Content = "(Auto - use first available)",
                    Tag = null
                });

                // Add codec options
                foreach (var decoder in decoders)
                {
                    decoderCombo.Items.Add(new ComboBoxItem
                    {
                        Content = decoder.Name,
                        Tag = decoder.Name
                    });
                }

                // Select current preference
                string? currentDecoder = _configuration.GetPreferredDecoder(extension);
                if (currentDecoder == null)
                {
                    decoderCombo.SelectedIndex = 0; // Auto
                }
                else
                {
                    var item = decoderCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag as string == currentDecoder);
                    if (item != null)
                    {
                        decoderCombo.SelectedItem = item;
                    }
                    else
                    {
                        decoderCombo.SelectedIndex = 0; // Fallback to Auto
                    }
                }

                Grid.SetRow(decoderCombo, 0);
                Grid.SetColumn(decoderCombo, 1);
                grid.Children.Add(decoderCombo);

                _decoderComboBoxes[extension] = decoderCombo;
            }

            // Encoder row
            if (encoders.Count > 0)
            {
                var encoderLabel = new TextBlock
                {
                    Text = "Encoder:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 10, 5)
                };
                Grid.SetRow(encoderLabel, 1);
                Grid.SetColumn(encoderLabel, 0);
                grid.Children.Add(encoderLabel);

                var encoderCombo = new ComboBox
                {
                    Margin = new Thickness(0, 5, 0, 5)
                };

                // Add "Auto" option
                encoderCombo.Items.Add(new ComboBoxItem
                {
                    Content = "(Auto - use first available)",
                    Tag = null
                });

                // Add codec options
                foreach (var encoder in encoders)
                {
                    encoderCombo.Items.Add(new ComboBoxItem
                    {
                        Content = encoder.Name,
                        Tag = encoder.Name
                    });
                }

                // Select current preference
                string? currentEncoder = _configuration.GetPreferredEncoder(extension);
                if (currentEncoder == null)
                {
                    encoderCombo.SelectedIndex = 0; // Auto
                }
                else
                {
                    var item = encoderCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag as string == currentEncoder);
                    if (item != null)
                    {
                        encoderCombo.SelectedItem = item;
                    }
                    else
                    {
                        encoderCombo.SelectedIndex = 0; // Fallback to Auto
                    }
                }

                Grid.SetRow(encoderCombo, 1);
                Grid.SetColumn(encoderCombo, 1);
                grid.Children.Add(encoderCombo);

                _encoderComboBoxes[extension] = encoderCombo;
            }

            groupBox.Content = grid;
            SettingsPanel.Children.Add(groupBox);
        }

        // Add a message if no codecs are registered
        if (allExtensions.Count == 0)
        {
            var message = new TextBlock
            {
                Text = "No codecs are currently registered.\n\n" +
                       "Codecs will appear here once you implement and register them " +
                       "in CodecRegistry.RegisterBuiltInCodecs().",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10),
                FontStyle = FontStyles.Italic
            };
            SettingsPanel.Children.Add(message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Save decoder preferences
        foreach (var kvp in _decoderComboBoxes)
        {
            string extension = kvp.Key;
            var comboBox = kvp.Value;

            if (comboBox.SelectedItem is ComboBoxItem item)
            {
                string? codecName = item.Tag as string;
                _configuration.SetPreferredDecoder(extension, codecName);
            }
        }

        // Save encoder preferences
        foreach (var kvp in _encoderComboBoxes)
        {
            string extension = kvp.Key;
            var comboBox = kvp.Value;

            if (comboBox.SelectedItem is ComboBoxItem item)
            {
                string? codecName = item.Tag as string;
                _configuration.SetPreferredEncoder(extension, codecName);
            }
        }

        // Persist to disk
        CodecRegistry.Instance.SaveConfiguration();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will reset all codec preferences to automatic selection.\n\nAre you sure?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Clear all preferences
            _configuration.DecodePreferences.Clear();
            _configuration.EncodePreferences.Clear();

            // Reset all combo boxes to "Auto" (first item)
            foreach (var comboBox in _decoderComboBoxes.Values)
            {
                comboBox.SelectedIndex = 0;
            }

            foreach (var comboBox in _encoderComboBoxes.Values)
            {
                comboBox.SelectedIndex = 0;
            }
        }
    }
}
