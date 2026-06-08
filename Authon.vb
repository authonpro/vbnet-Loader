' ╔══════════════════════════════════════════════════════════════════════════════╗
' ║  Authon VB.NET SDK — Software Licensing & Authentication                   ║
' ║  Version: 1.0.0                                                            ║
' ║  Dependencies: None (System.Net.Http, .NET 6+)                             ║
' ║                                                                            ║
' ║  Website: https://authon.pro                                               ║
' ║  Docs:    https://authon.pro/docs                                          ║
' ║  Discord: https://discord.gg/jMZCTKPsmE                                    ║
' ║  Status:  https://authon.pro/status                                        ║
' ║  Health:  https://api.authon.pro/health                                    ║
' ║  GitHub:  https://github.com/authonpro                                     ║
' ║                                                                            ║
' ║  Usage:                                                                    ║
' ║    Dim auth As New AuthonClient("app-id", "api-key")                       ║
' ║    Await auth.InitAsync()                                                  ║
' ║    Dim result = Await auth.LoginAsync("user", "pass")                      ║
' ║    If result.Success Then Console.WriteLine("Welcome " & auth.Username)    ║
' ╚══════════════════════════════════════════════════════════════════════════════╝

Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Management

Namespace AuthonSDK

    ''' <summary>
    ''' Represents a response from the Authon API.
    ''' </summary>
    Public Class AuthonResponse
        ''' <summary>Whether the request was successful.</summary>
        Public Property Success As Boolean = False
        ''' <summary>Response message (error description on failure).</summary>
        Public Property Message As String = ""
        ''' <summary>Response data as a dictionary.</summary>
        Public Property Data As Dictionary(Of String, Object) = Nothing
    End Class

    ''' <summary>
    ''' Main client for the Authon authentication and licensing API.
    ''' Provides async methods for initialization, authentication, session management,
    ''' variable storage, file downloads, and activity logging.
    ''' </summary>
    ''' <example>
    ''' <code>
    ''' Dim client As New AuthonClient("your-app-id", "your-api-key")
    ''' Dim initResult = Await client.InitAsync()
    ''' If initResult.Success Then
    '''     Dim loginResult = Await client.LoginAsync("username", "password")
    '''     If loginResult.Success Then
    '''         Console.WriteLine($"Welcome, {client.Username}! Level: {client.Level}")
    '''     End If
    ''' End If
    ''' </code>
    ''' </example>
    Public Class AuthonClient
        Implements IDisposable

#Region "Constants"
        ''' <summary>SDK version.</summary>
        Public Const VERSION As String = "1.0.0"
        ''' <summary>Default API URL.</summary>
        Public Const DEFAULT_API_URL As String = "https://api.authon.pro/v1"
        ''' <summary>Default timeout in seconds.</summary>
        Public Const DEFAULT_TIMEOUT As Integer = 15
#End Region

#Region "Private Fields"
        Private ReadOnly _appId As String
        Private ReadOnly _apiKey As String
        Private ReadOnly _apiUrl As String
        Private ReadOnly _httpClient As HttpClient
        Private _disposed As Boolean = False
#End Region

#Region "Public Properties - Session State"
        ''' <summary>Gets the current session token. Nothing if not authenticated.</summary>
        Public Property SessionToken As String
        ''' <summary>Gets the authenticated username.</summary>
        Public Property Username As String
        ''' <summary>Gets the user's access level (0+).</summary>
        Public Property Level As Integer = 0
        ''' <summary>Gets the subscription plan name.</summary>
        Public Property Subscription As String
        ''' <summary>Gets the subscription expiration date (ISO 8601).</summary>
        Public Property ExpiresAt As String
        ''' <summary>Gets whether the client has an active session.</summary>
        Public ReadOnly Property IsAuthenticated As Boolean
            Get
                Return Not String.IsNullOrEmpty(SessionToken)
            End Get
        End Property
#End Region

#Region "Public Properties - App Info"
        ''' <summary>Gets the application name (from InitAsync).</summary>
        Public Property AppName As String
        ''' <summary>Gets the application version (from InitAsync).</summary>
        Public Property AppVersion As String
        ''' <summary>Gets whether HWID locking is enabled.</summary>
        Public Property HwidLock As Boolean = False
        ''' <summary>Gets whether hash checking is enabled.</summary>
        Public Property HashCheck As Boolean = False
        ''' <summary>Gets whether InitAsync was called successfully.</summary>
        Public Property Initialized As Boolean = False
#End Region

#Region "Constructor"
        ''' <summary>
        ''' Creates a new Authon client instance.
        ''' </summary>
        ''' <param name="appId">Your Application ID from the Authon dashboard.</param>
        ''' <param name="apiKey">Your API Key from the Authon dashboard.</param>
        ''' <param name="apiUrl">Custom API URL (default: https://api.authon.pro/v1).</param>
        Public Sub New(appId As String, apiKey As String, Optional apiUrl As String = DEFAULT_API_URL)
            If String.IsNullOrWhiteSpace(appId) Then Throw New ArgumentNullException(NameOf(appId))
            If String.IsNullOrWhiteSpace(apiKey) Then Throw New ArgumentNullException(NameOf(apiKey))

            _appId = appId.Trim()
            _apiKey = apiKey.Trim()
            _apiUrl = If(apiUrl?.TrimEnd("/"c), DEFAULT_API_URL)

            _httpClient = New HttpClient()
            _httpClient.Timeout = TimeSpan.FromSeconds(DEFAULT_TIMEOUT)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"Authon-VBNET-SDK/{VERSION}")
        End Sub
#End Region

#Region "HWID Generation"
        ''' <summary>
        ''' Generates a hardware ID unique to the current machine.
        ''' Uses disk serial number + computer name, hashed with MD5.
        ''' </summary>
        ''' <returns>32-character lowercase hex MD5 hash.</returns>
        Public Shared Function GetHWID() As String
            Dim raw As String

            Try
                Dim serial As String = GetDiskSerial()
                raw = serial & Environment.MachineName
            Catch
                raw = Environment.MachineName & Environment.UserName
            End Try

            If String.IsNullOrEmpty(raw) Then
                raw = "fallback-" & Environment.MachineName
            End If

            Using md5Hash = MD5.Create()
                Dim bytes = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(raw))
                Dim sb As New StringBuilder(32)
                For Each b In bytes
                    sb.Append(b.ToString("x2"))
                Next
                Return sb.ToString()
            End Using
        End Function

        Private Shared Function GetDiskSerial() As String
            Try
                Using searcher As New ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index=0")
                    For Each obj As ManagementObject In searcher.Get()
                        Dim serial = obj("SerialNumber")?.ToString()?.Trim()
                        If Not String.IsNullOrEmpty(serial) Then Return serial
                    Next
                End Using
            Catch
                ' WMI not available
            End Try
            Return ""
        End Function
#End Region

#Region "API: Initialization"
        ''' <summary>
        ''' Initializes the connection to the Authon API.
        ''' Must be called before any other API method.
        ''' </summary>
        ''' <returns>AuthonResponse with app info on success.</returns>
        Public Async Function InitAsync() As Task(Of AuthonResponse)
            Dim payload As New Dictionary(Of String, Object) From {
                {"type", "init"}
            }

            Dim result = Await SendRequestAsync(payload)

            If result.Success AndAlso result.Data IsNot Nothing Then
                AppName = GetStr(result.Data, "name")
                AppVersion = GetStr(result.Data, "version")
                HwidLock = GetBool(result.Data, "hwidLock")
                HashCheck = GetBool(result.Data, "hashCheck")
                Initialized = True
            End If

            Return result
        End Function
#End Region

#Region "API: Authentication"
        ''' <summary>
        ''' Authenticates with username and password.
        ''' On success, sets SessionToken, Username, Level, Subscription, ExpiresAt.
        ''' </summary>
        ''' <param name="username">User's username.</param>
        ''' <param name="password">User's password.</param>
        ''' <param name="hwid">Hardware ID (auto-generated if Nothing).</param>
        ''' <returns>AuthonResponse indicating success or failure.</returns>
        Public Async Function LoginAsync(username As String, password As String, Optional hwid As String = Nothing) As Task(Of AuthonResponse)
            If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrWhiteSpace(password) Then
                Return New AuthonResponse With {.Success = False, .Message = "Username and password are required"}
            End If

            Dim payload As New Dictionary(Of String, Object) From {
                {"type", "login"},
                {"username", username},
                {"password", password},
                {"hwid", If(hwid, GetHWID())}
            }

            Dim result = Await SendRequestAsync(payload)
            If result.Success Then ExtractSession(result.Data)
            Return result
        End Function

        ''' <summary>
        ''' Authenticates using a license key only.
        ''' </summary>
        ''' <param name="licenseKey">The license key to validate/activate.</param>
        ''' <param name="hwid">Hardware ID (auto-generated if Nothing).</param>
        ''' <returns>AuthonResponse indicating success or failure.</returns>
        Public Async Function LicenseAsync(licenseKey As String, Optional hwid As String = Nothing) As Task(Of AuthonResponse)
            If String.IsNullOrWhiteSpace(licenseKey) Then
                Return New AuthonResponse With {.Success = False, .Message = "License key is required"}
            End If

            Dim payload As New Dictionary(Of String, Object) From {
                {"type", "license"},
                {"licenseKey", licenseKey},
                {"hwid", If(hwid, GetHWID())}
            }

            Dim result = Await SendRequestAsync(payload)
            If result.Success Then ExtractSession(result.Data)
            Return result
        End Function

        ''' <summary>
        ''' Registers a new user account with a license key.
        ''' </summary>
        Public Async Function RegisterAsync(username As String, password As String, licenseKey As String, Optional hwid As String = Nothing) As Task(Of AuthonResponse)
            If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrWhiteSpace(password) OrElse String.IsNullOrWhiteSpace(licenseKey) Then
                Return New AuthonResponse With {.Success = False, .Message = "Username, password, and licenseKey are required"}
            End If

            Return Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "register"},
                {"username", username},
                {"password", password},
                {"licenseKey", licenseKey},
                {"hwid", If(hwid, GetHWID())}
            })
        End Function
#End Region

#Region "API: Session Management"
        ''' <summary>Validates the current session (heartbeat).</summary>
        ''' <returns>True if session is valid.</returns>
        Public Async Function CheckAsync() As Task(Of Boolean)
            If Not IsAuthenticated Then Return False
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "check"}, {"sessionToken", SessionToken}
            })
            Return result.Success
        End Function

        ''' <summary>Ends the current session and clears local state.</summary>
        Public Async Function LogoutAsync() As Task(Of Boolean)
            If Not IsAuthenticated Then Return False
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "logout"}, {"sessionToken", SessionToken}
            })
            If result.Success Then ClearSession()
            Return result.Success
        End Function
#End Region

#Region "API: Variables"
        ''' <summary>Gets an application-level variable.</summary>
        Public Async Function GetVarAsync(key As String) As Task(Of String)
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "var"}, {"key", key}, {"sessionToken", SessionToken}
            })
            If result.Success AndAlso result.Data IsNot Nothing Then Return GetStr(result.Data, "value")
            Return Nothing
        End Function

        ''' <summary>Sets a user-level variable.</summary>
        Public Async Function SetVarAsync(key As String, value As String) As Task(Of Boolean)
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "setvar"}, {"key", key}, {"value", If(value, "")}, {"sessionToken", SessionToken}
            })
            Return result.Success
        End Function

        ''' <summary>Gets a user-level variable.</summary>
        Public Async Function GetUserVarAsync(key As String) As Task(Of String)
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "getvar"}, {"key", key}, {"sessionToken", SessionToken}
            })
            If result.Success AndAlso result.Data IsNot Nothing Then Return GetStr(result.Data, "value")
            Return Nothing
        End Function
#End Region

#Region "API: Files"
        ''' <summary>Lists files available to the authenticated user.</summary>
        Public Async Function ListFilesAsync() As Task(Of AuthonResponse)
            Return Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "list_files"}, {"sessionToken", SessionToken}
            })
        End Function

        ''' <summary>Downloads a file by ID and returns raw bytes.</summary>
        Public Async Function DownloadFileAsync(fileId As String) As Task(Of Byte())
            If Not IsAuthenticated OrElse String.IsNullOrWhiteSpace(fileId) Then Return Nothing

            Try
                Dim payload As New Dictionary(Of String, Object) From {
                    {"type", "file"}, {"appId", _appId}, {"apiKey", _apiKey},
                    {"fileId", fileId}, {"sessionToken", SessionToken}
                }
                Dim json = JsonSerializer.Serialize(payload)
                Using content As New StringContent(json, Encoding.UTF8, "application/json")
                    Dim response = Await _httpClient.PostAsync(_apiUrl, content)
                    Dim ct = response.Content.Headers.ContentType?.MediaType
                    If ct = "application/octet-stream" Then
                        Return Await response.Content.ReadAsByteArrayAsync()
                    End If
                End Using

                ' GET fallback
                Dim url = $"{_apiUrl}/files/download/{fileId}?token={SessionToken}"
                Dim getResp = Await _httpClient.GetAsync(url)
                If getResp.Content.Headers.ContentType?.MediaType = "application/octet-stream" Then
                    Return Await getResp.Content.ReadAsByteArrayAsync()
                End If
            Catch
                ' Fall through
            End Try

            Return Nothing
        End Function
#End Region

#Region "API: Logging & Analytics"
        ''' <summary>Sends an activity log message to the dashboard.</summary>
        Public Async Function LogAsync(message As String) As Task(Of Boolean)
            Dim msg = If(message?.Length > 500, message.Substring(0, 500), message)
            Dim result = Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "log"}, {"message", msg}, {"sessionToken", SessionToken}
            })
            Return result.Success
        End Function

        ''' <summary>Gets the list of currently online users.</summary>
        Public Async Function FetchOnlineAsync() As Task(Of AuthonResponse)
            Return Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "fetch_online"}, {"sessionToken", SessionToken}
            })
        End Function

        ''' <summary>Gets application statistics.</summary>
        Public Async Function FetchStatsAsync() As Task(Of AuthonResponse)
            Return Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "fetch_stats"}, {"sessionToken", SessionToken}
            })
        End Function
#End Region

#Region "API: Security"
        ''' <summary>Checks if an IP or HWID is blacklisted.</summary>
        Public Async Function CheckBlacklistAsync(Optional ip As String = Nothing, Optional hwid As String = Nothing) As Task(Of AuthonResponse)
            Dim payload As New Dictionary(Of String, Object) From {{"type", "check_blacklist"}}
            If Not String.IsNullOrWhiteSpace(ip) Then payload("ip") = ip
            If Not String.IsNullOrWhiteSpace(hwid) Then payload("hwid") = hwid
            Return Await SendRequestAsync(payload)
        End Function

        ''' <summary>Redeems a referral code for bonus subscription days.</summary>
        Public Async Function RedeemReferralAsync(code As String) As Task(Of AuthonResponse)
            Return Await SendRequestAsync(New Dictionary(Of String, Object) From {
                {"type", "redeem_referral"}, {"code", code}, {"sessionToken", SessionToken}
            })
        End Function
#End Region

#Region "Internal"
        Private Async Function SendRequestAsync(payload As Dictionary(Of String, Object)) As Task(Of AuthonResponse)
            Try
                payload("appId") = _appId
                payload("apiKey") = _apiKey

                Dim json = JsonSerializer.Serialize(payload)
                Using content As New StringContent(json, Encoding.UTF8, "application/json")
                    Dim response = Await _httpClient.PostAsync(_apiUrl, content)
                    Dim body = Await response.Content.ReadAsStringAsync()
                    Return ParseResponse(body)
                End Using
            Catch ex As HttpRequestException
                Return New AuthonResponse With {.Success = False, .Message = "Connection failed. Check https://authon.pro/status"}
            Catch ex As TaskCanceledException
                Return New AuthonResponse With {.Success = False, .Message = "Request timed out"}
            Catch ex As Exception
                Return New AuthonResponse With {.Success = False, .Message = $"Unexpected error: {ex.Message}"}
            End Try
        End Function

        Private Shared Function ParseResponse(body As String) As AuthonResponse
            Dim result As New AuthonResponse()
            Try
                Using doc = JsonDocument.Parse(body)
                    Dim root = doc.RootElement

                    If root.TryGetProperty("success", result.Success) Then
                        ' parsed
                    End If
                    result.Success = If(root.TryGetProperty("success", Nothing), root.GetProperty("success").GetBoolean(), False)
                    result.Message = If(root.TryGetProperty("message", Nothing), root.GetProperty("message").GetString(), "")

                    If root.TryGetProperty("data", Nothing) Then
                        Dim dataEl = root.GetProperty("data")
                        If dataEl.ValueKind = JsonValueKind.Object Then
                            result.Data = New Dictionary(Of String, Object)
                            For Each prop In dataEl.EnumerateObject()
                                Select Case prop.Value.ValueKind
                                    Case JsonValueKind.String
                                        result.Data(prop.Name) = prop.Value.GetString()
                                    Case JsonValueKind.Number
                                        result.Data(prop.Name) = prop.Value.GetInt32()
                                    Case JsonValueKind.True
                                        result.Data(prop.Name) = True
                                    Case JsonValueKind.False
                                        result.Data(prop.Name) = False
                                    Case Else
                                        result.Data(prop.Name) = prop.Value.GetRawText()
                                End Select
                            Next
                        End If
                    End If
                End Using
            Catch
                result.Success = False
                result.Message = "Failed to parse response"
            End Try
            Return result
        End Function

        Private Sub ExtractSession(data As Dictionary(Of String, Object))
            If data Is Nothing Then Return
            SessionToken = GetStr(data, "sessionToken")
            Username = GetStr(data, "username")
            Level = GetInt(data, "level")
            Subscription = GetStr(data, "subscription")
            ExpiresAt = GetStr(data, "expiresAt")
        End Sub

        Private Sub ClearSession()
            SessionToken = Nothing
            Username = Nothing
            Level = 0
            Subscription = Nothing
            ExpiresAt = Nothing
        End Sub

        Private Shared Function GetStr(data As Dictionary(Of String, Object), key As String) As String
            If data.ContainsKey(key) AndAlso data(key) IsNot Nothing Then Return data(key).ToString()
            Return Nothing
        End Function

        Private Shared Function GetInt(data As Dictionary(Of String, Object), key As String) As Integer
            If data.ContainsKey(key) Then
                Dim val = data(key)
                If TypeOf val Is Integer Then Return CInt(val)
                If TypeOf val Is String Then
                    Dim result As Integer
                    If Integer.TryParse(CStr(val), result) Then Return result
                End If
            End If
            Return 0
        End Function

        Private Shared Function GetBool(data As Dictionary(Of String, Object), key As String) As Boolean
            If data.ContainsKey(key) Then
                Dim val = data(key)
                If TypeOf val Is Boolean Then Return CBool(val)
                If TypeOf val Is String Then Return Boolean.Parse(CStr(val))
            End If
            Return False
        End Function
#End Region

#Region "IDisposable"
        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _httpClient?.Dispose()
                _disposed = True
            End If
        End Sub
#End Region

    End Class

End Namespace
