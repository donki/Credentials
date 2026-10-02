namespace Credentials.Platforms.Windows;

/// <summary>
/// La logica del icono de bandeja: que hacer con cada mensaje de la ventana (minimizar a la
/// bandeja, clic en el icono, menu Abrir/Salir, bloqueo y vuelta de la sesion de Windows, el
/// «enseñate» de otra instancia). Lo que toca Windows (Shell_NotifyIcon, ShowWindow, el menu) lo
/// pone TrayIcon; en las pruebas, un doble que apunta las llamadas.
/// </summary>
public abstract class TrayController(uint showMessage, Action exit)
{
    public const int WmSysCommand = 0x0112;
    public const int WmCommand = 0x0111;
    public const int WmRButtonUp = 0x0205;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDblClk = 0x0203;
    public const int WmTray = 0x8001;   // WM_APP + 1
    public const int ScMinimize = 0xF020;
    public const int WmWtsSessionChange = 0x02B1;
    public const int WtsSessionLock = 0x7;
    public const int WtsSessionUnlock = 0x8;
    public const int IdOpen = 1, IdExit = 2;
    public const int SwHide = 0, SwRestore = 9;

    private bool _shown;

    /// <summary>Si al minimizar se esconde en la bandeja (ajuste del usuario) o se minimiza como siempre.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>El usuario ha bloqueado la sesion de Windows (Win+L, o el bloqueo automatico del sistema).</summary>
    public event Action? SessionLocked;

    /// <summary>El usuario ha vuelto a la sesion de Windows (tras Win+L o la pantalla de bloqueo).</summary>
    public event Action? SessionUnlocked;

    /// <summary>La ventana esta escondida en la bandeja (solo se ve el icono).</summary>
    public bool IsHidden => _shown;

    /// <summary>Esconder ahora (arranque con --tray).</summary>
    public void HideToTray() => Hide();

    /// <summary>Traer la ventana al frente (desde la bandeja o desde detras de otras).</summary>
    public void Show() => Restore();

    /// <summary>Cuanto lleva el PC sin teclado ni raton, con los milisegundos de GetTickCount y GetLastInputInfo (dan la vuelta a los 49 dias).</summary>
    public static TimeSpan IdleTime(uint nowTicks, uint lastInputTicks) => TimeSpan.FromMilliseconds(unchecked(nowTicks - lastInputTicks));

    /// <summary>Atiende un mensaje de la ventana; false si no es suyo y hay que pasarlo al procedimiento de antes.</summary>
    public bool Handle(uint msg, long wParam, long lParam)
    {
        switch (msg)
        {
            case WmSysCommand when (wParam & 0xFFF0) == ScMinimize && MinimizeToTray:
                Hide();
                return true;
            case WmTray:
                var evt = (int)(lParam & 0xFFFF);
                if (evt is WmLButtonUp or WmLButtonDblClk)
                    Restore();
                else if (evt == WmRButtonUp)
                    ShowMenu();
                return true;
            case WmCommand when (int)(wParam & 0xFFFF) == IdOpen:
                Restore();
                return true;
            case WmCommand when (int)(wParam & 0xFFFF) == IdExit:
                Remove();
                exit();
                return true;
            case WmWtsSessionChange when wParam == WtsSessionLock:
                SessionLocked?.Invoke();
                return false;
            case WmWtsSessionChange when wParam == WtsSessionUnlock:
                SessionUnlocked?.Invoke();
                return false;
            case var m when m == showMessage:
                // Otra instancia ha arrancado: esta se enseña en su lugar y se lo confirma (si no
                // contestara, la otra arrancaria igual para que el usuario no se quede sin ventana).
                Restore();
                NotifyShown();
                return true;
        }
        return false;
    }

    private void Hide()
    {
        Add();
        ShowWindow(SwHide);
    }

    private void Restore()
    {
        ShowWindow(SwRestore);
        BringToForeground();
        Remove();
    }

    private void Add()
    {
        if (_shown)
            return;
        AddIcon();
        _shown = true;
    }

    private void Remove()
    {
        if (!_shown)
            return;
        RemoveIcon();
        _shown = false;
    }

    /// <summary>Pone el icono en el area de notificacion.</summary>
    protected abstract void AddIcon();

    /// <summary>Quita el icono del area de notificacion.</summary>
    protected abstract void RemoveIcon();

    protected abstract void ShowWindow(int command);

    protected abstract void BringToForeground();

    /// <summary>El menu del boton derecho (Abrir, Salir) donde esta el raton.</summary>
    protected abstract void ShowMenu();

    /// <summary>Confirma a la otra instancia que esta ya se ha enseñado.</summary>
    protected abstract void NotifyShown();
}
