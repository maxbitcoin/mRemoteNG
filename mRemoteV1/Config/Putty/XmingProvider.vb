﻿Imports System.IO
Imports mRemoteNG.App
Imports mRemoteNG.Messages
Imports mRemoteNG.Connection.Protocol

Namespace Config.Putty
    Public Class XmingProvider
        Inherits Provider

        Private Shared _eventWatcher As FileSystemWatcher

        Private Shared Function GetPuttyConfPath() As String
            Dim puttyPath As String
            If My.Settings.UseCustomPuttyPath Then
                puttyPath = My.Settings.CustomPuttyPath
            Else
                puttyPath = Info.General.PuttyPath
            End If
            Return Path.Combine(Path.GetDirectoryName(puttyPath), "putty.conf")
        End Function

        Private Shared Function GetSessionsFolderPath() As String
            Dim puttyConfPath As String = GetPuttyConfPath()
            Dim sessionFileReader As New PuttyConfFileReader(puttyConfPath)
            Dim basePath As String = Environment.ExpandEnvironmentVariables(sessionFileReader.GetValue("sshk&sess"))
            Return Path.Combine(basePath, "sessions")
        End Function

        Public Overrides Function GetSessionNames(Optional ByVal raw As Boolean = False) As String()
            Dim sessionsFolderPath As String = GetSessionsFolderPath()
            If Not Directory.Exists(sessionsFolderPath) Then Return New String() {}

            Dim sessionNames As New List(Of String)
            For Each sessionName As String In Directory.GetFiles(sessionsFolderPath)
                sessionName = Path.GetFileName(sessionName)
                If raw Then
                    sessionNames.Add(sessionName)
                Else
                    sessionNames.Add(Web.HttpUtility.UrlDecode(sessionName.Replace("+", "%2B")))
                End If
            Next

            If sessionNames.Contains("Default%20Settings") Then ' Do not localize
                sessionNames.Remove("Default%20Settings")
            End If
            If sessionNames.Contains("Default Settings") Then
                sessionNames.Remove("Default Settings")
            End If

            Return sessionNames.ToArray()
        End Function

        Public Overrides Function GetSession(ByVal sessionName As String) As Connection.PuttySession.Info
            Dim sessionsFolderPath As String = GetSessionsFolderPath()
            If Not Directory.Exists(sessionsFolderPath) Then Return Nothing

            Dim sessionFile As String = Path.Combine(sessionsFolderPath, sessionName)
            If Not File.Exists(sessionFile) Then Return Nothing

            sessionName = Web.HttpUtility.UrlDecode(sessionName.Replace("+", "%2B"))

            Dim sessionFileReader As New SessionFileReader(sessionFile)
            Dim sessionInfo As New Connection.PuttySession.Info
            With sessionInfo
                .PuttySession = sessionName
                .Name = sessionName
                .Hostname = sessionFileReader.GetValue("HostName")
                .Username = sessionFileReader.GetValue("UserName")
                Dim protocol As String = sessionFileReader.GetValue("Protocol")
                If protocol Is Nothing Then protocol = "ssh"
                Select Case protocol.ToLowerInvariant()
                    Case "raw"
                        .Protocol = Protocols.RAW
                    Case "rlogin"
                        .Protocol = Protocols.Rlogin
                    Case "serial"
                        Return Nothing
                    Case "ssh"
                        Dim sshVersionObject As Object = sessionFileReader.GetValue("SshProt")
                        If sshVersionObject IsNot Nothing Then
                            Dim sshVersion As Integer = CType(sshVersionObject, Integer)
                            If sshVersion >= 2 Then
                                .Protocol = Protocols.SSH2
                            Else
                                .Protocol = Protocols.SSH1
                            End If
                        Else
                            .Protocol = Protocols.SSH2
                        End If
                    Case "telnet"
                        .Protocol = Protocols.Telnet
                    Case Else
                        Return Nothing
                End Select
                .Port = sessionFileReader.GetValue("PortNumber")
            End With

            Return sessionInfo
        End Function

        Public Overrides Sub StartWatcher()
            If _eventWatcher IsNot Nothing Then Return

            Try
                _eventWatcher = New FileSystemWatcher(GetSessionsFolderPath())
                _eventWatcher.NotifyFilter = (NotifyFilters.FileName Or NotifyFilters.LastWrite)
                AddHandler _eventWatcher.Changed, AddressOf OnFileSystemEventArrived
                AddHandler _eventWatcher.Created, AddressOf OnFileSystemEventArrived
                AddHandler _eventWatcher.Deleted, AddressOf OnFileSystemEventArrived
                AddHandler _eventWatcher.Renamed, AddressOf OnFileSystemEventArrived
                _eventWatcher.EnableRaisingEvents = True
            Catch ex As Exception
                Runtime.MessageCollector.AddExceptionMessage("XmingPortablePuttySessions.Watcher.StartWatching() failed.", ex, MessageClass.WarningMsg, True)
            End Try
        End Sub

        Public Overrides Sub StopWatcher()
            If _eventWatcher Is Nothing Then Return
            _eventWatcher.EnableRaisingEvents = False
            _eventWatcher.Dispose()
            _eventWatcher = Nothing
        End Sub

        Private Sub OnFileSystemEventArrived(ByVal sender As Object, ByVal e As FileSystemEventArgs)
            OnSessionChanged(New SessionChangedEventArgs())
        End Sub

        Private Class PuttyConfFileReader
            Public Sub New(ByVal puttyConfFile As String)
                _puttyConfFile = puttyConfFile
            End Sub

            Private ReadOnly _puttyConfFile As String
            Private _configurationLoaded As Boolean = False
            Private ReadOnly _configuration As New Dictionary(Of String, String)

            Private Sub LoadConfiguration()
                _configurationLoaded = True
                Try
                    If Not File.Exists(_puttyConfFile) Then Return
                    Using streamReader As New StreamReader(_puttyConfFile)
                        Dim line As String
                        Do
                            line = streamReader.ReadLine()
                            If line Is Nothing Then Exit Do
                            line = line.Trim()
                            If line = String.Empty Then Continue Do ' Blank line
                            If line.Substring(0, 1) = ";" Then Continue Do ' Comment
                            Dim parts() As String = line.Split(New Char() {"="}, 2)
                            If parts.Length < 2 Then Continue Do
                            If _configuration.ContainsKey(parts(0)) Then Continue Do ' As per http://www.straightrunning.com/XmingNotes/portableputty.php only first entry is used
                            _configuration.Add(parts(0), parts(1))
                        Loop
                    End Using
                Catch ex As Exception
                    Runtime.MessageCollector.AddExceptionMessage("PuttyConfFileReader.LoadConfiguration() failed.", ex, MessageClass.ErrorMsg, True)
                End Try
            End Sub

            Public Function GetValue(ByVal setting As String) As String
                If Not _configurationLoaded Then LoadConfiguration()
                If Not _configuration.ContainsKey(setting) Then Return String.Empty
                Return _configuration(setting)
            End Function
        End Class

        Private Class SessionFileReader
            Public Sub New(ByVal sessionFile As String)
                _sessionFile = sessionFile
            End Sub

            Private ReadOnly _sessionFile As String
            Private _sessionInfoLoaded As Boolean = False
            Private ReadOnly _sessionInfo As New Dictionary(Of String, String)

            Private Sub LoadSessionInfo()
                _sessionInfoLoaded = True
                Try
                    If Not File.Exists(_sessionFile) Then Return
                    Using streamReader As New StreamReader(_sessionFile)
                        Dim line As String
                        Do
                            line = streamReader.ReadLine()
                            If line Is Nothing Then Exit Do
                            Dim parts() As String = line.Split(New Char() {"\"})
                            If parts.Length < 2 Then Continue Do
                            _sessionInfo.Add(parts(0), parts(1))
                        Loop
                    End Using
                Catch ex As Exception
                    Runtime.MessageCollector.AddExceptionMessage("SessionFileReader.LoadSessionInfo() failed.", ex, MessageClass.ErrorMsg, True)
                End Try
            End Sub

            Public Function GetValue(ByVal setting As String) As String
                If Not _sessionInfoLoaded Then LoadSessionInfo()
                If Not _sessionInfo.ContainsKey(setting) Then Return String.Empty
                Return _sessionInfo(setting)
            End Function
        End Class
    End Class
End Namespace
