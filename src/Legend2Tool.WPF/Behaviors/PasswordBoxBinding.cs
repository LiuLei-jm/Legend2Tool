using System.Windows;
using System.Windows.Controls;

namespace Legend2Tool.WPF.Behaviors
{
    public static class PasswordBoxBinding
    {
        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxBinding),
                new PropertyMetadata(false, OnBindPasswordChanged)
            );

        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxBinding),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBoundPasswordChanged
                )
            );

        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating",
                typeof(bool),
                typeof(PasswordBoxBinding),
                new PropertyMetadata(false)
            );

        public static bool GetBindPassword(DependencyObject element) =>
            (bool)element.GetValue(BindPasswordProperty);

        public static void SetBindPassword(DependencyObject element, bool value) =>
            element.SetValue(BindPasswordProperty, value);

        public static string GetBoundPassword(DependencyObject element) =>
            (string)element.GetValue(BoundPasswordProperty);

        public static void SetBoundPassword(DependencyObject element, string value) =>
            element.SetValue(BoundPasswordProperty, value);

        private static bool GetIsUpdating(DependencyObject element) =>
            (bool)element.GetValue(IsUpdatingProperty);

        private static void SetIsUpdating(DependencyObject element, bool value) =>
            element.SetValue(IsUpdatingProperty, value);

        private static void OnBindPasswordChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs
        )
        {
            if (dependencyObject is not PasswordBox passwordBox)
            {
                return;
            }

            passwordBox.PasswordChanged -= OnPasswordChanged;
            if (eventArgs.NewValue is true)
            {
                passwordBox.PasswordChanged += OnPasswordChanged;
            }
        }

        private static void OnBoundPasswordChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs eventArgs
        )
        {
            if (
                dependencyObject is not PasswordBox passwordBox
                || !GetBindPassword(passwordBox)
                || GetIsUpdating(passwordBox)
            )
            {
                return;
            }

            passwordBox.PasswordChanged -= OnPasswordChanged;
            passwordBox.Password = eventArgs.NewValue as string ?? string.Empty;
            passwordBox.PasswordChanged += OnPasswordChanged;
        }

        private static void OnPasswordChanged(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not PasswordBox passwordBox)
            {
                return;
            }

            SetIsUpdating(passwordBox, true);
            SetBoundPassword(passwordBox, passwordBox.Password);
            SetIsUpdating(passwordBox, false);
        }
    }
}
