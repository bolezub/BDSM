using MaterialDesignThemes.Wpf;

namespace BDSM
{
    public static class NotificationService
    {
        private static SnackbarMessageQueue? _messageQueue;

        /// <summary>
        /// Registers the main message queue from the UI so the service can use it.
        /// </summary>
        public static void RegisterSnackbar(SnackbarMessageQueue queue)
        {
            _messageQueue = queue;
        }

        /// <summary>
        /// Shows an informational message to the user.
        /// </summary>
        public static void ShowInfo(string message)
        {
            if (_messageQueue != null)
            {
                // The message will be displayed for 3 seconds.
                _messageQueue.Enqueue(message);
            }
        }
    }
}