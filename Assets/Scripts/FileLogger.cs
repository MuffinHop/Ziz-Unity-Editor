using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Centralized file-based logging system that writes all log messages to a .txt file
/// instead of using Debug.Log. Useful for tracking detailed export/recording operations.
/// </summary>
public static class FileLogger
{
    private static string _logFilePath;
    private static StreamWriter _logWriter;
    private static object _lockObject = new object();
    private static bool _isInitialized = false;
    private static StringBuilder _buffer = new StringBuilder();
    private const int BUFFER_FLUSH_SIZE = 10; // Flush after every 10 lines

    /// <summary>
    /// Initializes the file logger and opens the log file for writing
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            string projectRoot = Application.dataPath.Replace("/Assets", "");
            string logsDir = Path.Combine(projectRoot, "Logs");
            
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _logFilePath = Path.Combine(logsDir, $"export_log_{timestamp}.txt");

            _logWriter = new StreamWriter(_logFilePath, false, Encoding.UTF8)
            {
                AutoFlush = false
            };

            _isInitialized = true;
            Log($"File logger initialized");
            Log($"Log file: {_logFilePath}");
            Log($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            Log($"Unity Version: {Application.unityVersion}");
            Log($"Platform: {Application.platform}");
            Log("");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize FileLogger: {ex.Message}");
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Logs a message to the file with timestamp
    /// </summary>
    public static void Log(string message)
    {
        if (!_isInitialized) Initialize();

        lock (_lockObject)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logLine = $"[{timestamp}] {message}";
                
                _buffer.AppendLine(logLine);

                // Flush buffer periodically
                if (_buffer.ToString().Split('\n').Length >= BUFFER_FLUSH_SIZE)
                {
                    _logWriter?.Write(_buffer.ToString());
                    _buffer.Clear();
                    _logWriter?.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"FileLogger error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Logs a warning message (with [WARNING] prefix)
    /// </summary>
    public static void LogWarning(string message)
    {
        Log($"[WARNING] {message}");
    }

    /// <summary>
    /// Logs an error message (with [ERROR] prefix)
    /// </summary>
    public static void LogError(string message)
    {
        Log($"[ERROR] {message}");
    }

    /// <summary>
    /// Logs a section header for organization
    /// </summary>
    public static void LogSection(string sectionName)
    {
        Log("");
    Log($"{sectionName}:");
    }

    /// <summary>
    /// Logs detailed information (with [INFO] prefix)
    /// </summary>
    public static void LogInfo(string message)
    {
        Log($"[INFO] {message}");
    }

    /// <summary>
    /// Logs debug information (with [DEBUG] prefix)
    /// </summary>
    public static void LogDebug(string message)
    {
        Log($"[DEBUG] {message}");
    }

    /// <summary>
    /// Flushes any remaining buffered content to the file
    /// </summary>
    public static void Flush()
    {
        lock (_lockObject)
        {
            try
            {
                if (_buffer.Length > 0)
                {
                    _logWriter?.Write(_buffer.ToString());
                    _buffer.Clear();
                }
                _logWriter?.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"FileLogger flush error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Closes the log file and finalizes logging
    /// </summary>
    public static void Close()
    {
        lock (_lockObject)
        {
            try
            {
                if (_logWriter != null)
                {
                    Log("");
                    Log($"Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    Log("Log session complete");
                    
                    Flush();
                    _logWriter.Close();
                    _logWriter.Dispose();
                    _logWriter = null;
                }
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"FileLogger close error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the current log file path
    /// </summary>
    public static string GetLogFilePath()
    {
        return _logFilePath ?? "Not initialized";
    }

    /// <summary>
    /// Logs a formatted line with multiple values
    /// </summary>
    public static void LogFormat(string format, params object[] args)
    {
        Log(string.Format(format, args));
    }

    /// <summary>
    /// Opens the log file in the default text editor (editor only)
    /// </summary>
    public static void OpenLogFile()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(_logFilePath))
        {
            Debug.LogError("No log file has been created yet");
            return;
        }

        if (!File.Exists(_logFilePath))
        {
            Debug.LogError($"Log file not found: {_logFilePath}");
            return;
        }

        System.Diagnostics.Process.Start(_logFilePath);
        Log("Log file opened in default editor");
#endif
    }
}
