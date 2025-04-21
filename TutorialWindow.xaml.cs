using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace VarIntCalculator
{
    public partial class TutorialWindow : Window
    {
        private int currentStep = 0;
        private List<string> tutorialSteps;

        public TutorialWindow()
        {
            InitializeComponent();
            InitializeTutorialSteps();
            DisplayCurrentStep();
        }

        private void InitializeTutorialSteps()
        {
            tutorialSteps = new List<string>
            {
                "<h2>Step 1: Convert each hex value to binary</h2><p>Example Hex: 81 20</p><ul><li>81 -> <span style='color: #00ff00;'>10000001</span></li><li>20 -> <span style='color: #00ff00;'>00100000</span></li></ul>",
                "<h2>Step 2: Drop the most significant bit (MSB) from each byte</h2><ul><li><span style='color: #ff6347;'>10000001</span> -> <span style='color: #00ff00;'>0000001</span></li><li><span style='color: #ff6347;'>00100000</span> -> <span style='color: #00ff00;'>0100000</span></li></ul>",
                "<h2>Step 3: Concatenate the remaining bits</h2><p>Result: <span style='color: #00ff00;'>0000001 0100000</span></p>",
                "<h2>Step 4: Pad with zeros on the left as needed to form the correct number</h2><p>Result: <span style='color: #00ff00;'>00000000 10100000</span></p>",
                "<h2>Step 5: Convert the binary value to hex</h2><p><span style='color: #00ff00;'>00000000 10100000</span> -> 00A0</p>",
                "<h2>Step 6: Finally, convert hex to decimal</h2><p>00A0 -> 160</p><p>The decimal representation of the varint value is: <strong>160</strong></p>"
            };
        }

        private void DisplayCurrentStep()
        {
            // Convert HTML-like string to rich text
            TutorialContent.Inlines.Clear();
            
            string htmlContent = tutorialSteps[currentStep]
                .Replace("<h2>", "")
                .Replace("</h2>", "\n\n")
                .Replace("<p>", "")
                .Replace("</p>", "\n")
                .Replace("<ul>", "\n")
                .Replace("</ul>", "\n")
                .Replace("<li>", "• ")
                .Replace("</li>", "\n")
                .Replace("<strong>", "")
                .Replace("</strong>", "");

            // Handle colored spans
            while (htmlContent.Contains("<span style='color: "))
            {
                int spanStart = htmlContent.IndexOf("<span style='color: ");
                int colorEnd = htmlContent.IndexOf(";'>", spanStart);
                int spanEnd = htmlContent.IndexOf("</span>", colorEnd);
                
                if (spanStart >= 0 && colorEnd >= 0 && spanEnd >= 0)
                {
                    string colorValue = htmlContent.Substring(spanStart + 19, colorEnd - (spanStart + 19));
                    string spanContent = htmlContent.Substring(colorEnd + 3, spanEnd - (colorEnd + 3));
                    
                    // Get text before span
                    string beforeSpan = htmlContent.Substring(0, spanStart);
                    // Get text after span
                    string afterSpan = htmlContent.Substring(spanEnd + 7);
                    
                    // Create a Run with the content before the span
                    if (!string.IsNullOrEmpty(beforeSpan))
                    {
                        TutorialContent.Inlines.Add(new Run(beforeSpan));
                    }
                    
                    // Create a Run with the span content and its color
                    Run coloredRun = new Run(spanContent);
                    coloredRun.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorValue));
                    TutorialContent.Inlines.Add(coloredRun);
                    
                    // Continue with the remaining content
                    htmlContent = afterSpan;
                }
                else
                {
                    break;
                }
            }
            
            // Add any remaining text
            if (!string.IsNullOrEmpty(htmlContent))
            {
                TutorialContent.Inlines.Add(new Run(htmlContent));
            }

            // Update navigation buttons
            PreviousButton.IsEnabled = currentStep > 0;
            NextButton.IsEnabled = currentStep < tutorialSteps.Count - 1;
            NextButton.Content = currentStep < tutorialSteps.Count - 1 ? "Next" : "Finish";
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                currentStep--;
                DisplayCurrentStep();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentStep < tutorialSteps.Count - 1)
            {
                currentStep++;
                DisplayCurrentStep();
            }
            else
            {
                Close();
            }
        }
    }
} 