using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Credentials.Services;

/// <inheritdoc cref="ILocalizationService"/>
public class LocalizationService : ILocalizationService
{
    /// <summary>Nombre visible de la aplicacion: «Credentials» en Android y «sOC Credentials» en Windows y la Store.</summary>
    public static readonly string AppName = OperatingSystem.IsAndroid() ? "Credentials" : "sOC Credentials";

    public const string SystemLanguage = "";
    public const string DefaultLanguage = "en";

    private readonly ISettingsService _settings;
    private readonly ILogger<LocalizationService> _logger;
    private string _current = DefaultLanguage;

    public LocalizationService(ISettingsService settings, ILogger<LocalizationService> logger)
    {
        _settings = settings;
        _logger = logger;
        SetLanguage(_settings.Language);
    }

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _current;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    public string this[string key]
    {
        get
        {
            var table = _current == "es" ? Spanish : English;
            if (table.TryGetValue(key, out var value))
                return value;

            if (English.TryGetValue(key, out var fallback))
            {
                _logger.LogWarning("Missing {Language} translation for key {Key}", _current, key);
                return fallback;
            }

            _logger.LogWarning("Unknown translation key {Key}", key);
            return key;
        }
    }

    public void SetLanguage(string? languageCode)
    {
        var resolved = Resolve(languageCode);
        if (resolved == _current && CurrentCulture is not null)
            return;

        _current = resolved;
        CurrentCulture = CultureInfo.GetCultureInfo(resolved);

        // Los formatos sensibles a la cultura siguen el idioma elegido (constitucion 8).
        CultureInfo.DefaultThreadCurrentCulture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CurrentCulture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resuelve el idioma efectivo: el elegido por el usuario si esta soportado; si se pide
    /// seguir al sistema, el del sistema cuando este soportado; en cualquier otro caso, ingles.
    /// </summary>
    private static string Resolve(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            return IsSupported(languageCode) ? languageCode : DefaultLanguage;

        try
        {
            var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return IsSupported(system) ? system : DefaultLanguage;
        }
        catch (Exception)
        {
            return DefaultLanguage;
        }
    }

    private static bool IsSupported(string code) => code is "es" or "en";

    private static readonly Dictionary<string, string> English = new()
    {
        ["Company"] = "Socratic",

        ["MenuHome"] = "Home",
        ["About"] = "About",




        ["Continue"] = "Continue",
        ["NotNow"] = "Not now",
        ["ExtSection"] = "Browser extensions",
        ["AutofillSection"] = "Autofill",
        ["AutofillIsOurs"] = "sOC Credentials fills in passwords on this device.",
        ["AutofillIsOther"] = "Another password manager (or none) fills in passwords on this device. Android allows only one: choosing sOC Credentials disconnects the other one.",
        ["AutofillUseThis"] = "Use sOC Credentials to autofill",
        ["AutofillChange"] = "Change autofill service",
        ["AutofillPreferredHint"] = "In browsers (Edge, Chrome…) the one in charge is the system's preferred password service, not the autofill one: while Google is set there, Google is what the browser offers.",
        ["AutofillPreferredOpen"] = "Preferred password service",
        ["AutofillPreferredNoScreen"] = "This device does not have that screen.",
        ["AutofillBrowsersHint"] = "Each browser also has its own password manager, and it wins inside it. Turn it off in the browser's own settings.",
        ["AutofillBrowsersOpen"] = "Open the browser settings",
        ["AutofillBrowserSteps"] = "In {0}: Settings › Passwords (or Autofill) → turn off saving and filling passwords, and if there is an option to use another service, choose sOC Credentials. Then come back.",
        ["AutofillOfferTitle"] = "Another password manager (or none) is filling in your passwords. Use sOC Credentials instead? The other one gets disconnected.",
        ["AutofillAskOnUnlock"] = "Offer it when unlocking",
        ["AutofillAskOnUnlockHint"] = "While another manager is the autofill service.",
        ["DesktopAutofill"] = "Fill in Windows apps",
        ["DesktopAutofillHint"] = "When a password field of a program gets the focus, a list with the matching entries appears next to it (websites use the browser extension).",
        ["DesktopFillHint"] = "Click to type the user name and the password.",
        ["ExtSectionHint"] = "Fills usernames, passwords and 2FA codes in Edge, Chrome and Firefox, and offers to save new ones. It talks to this app: nothing leaves your PC.",
        ["ExtInstalled"] = "Installed",
        ["ExtNotInstalled"] = "Not installed",
        ["ExtInstallButton"] = "Install…",
        ["ExtAskOnUnlock"] = "Offer to install it when unlocking",
        ["ExtAskOnUnlockHint"] = "Only for browsers on this PC that do not have it yet.",
        ["ExtOfferTitle"] = "{0}: the sOC Credentials extension is not installed. Install it now?",
        ["ExtInstallNow"] = "Install now",
        ["ExtDontAsk"] = "Don't ask again",
        ["ExtInstallIn"] = "Install in {0}",
        ["ExtOpenBrowser"] = "Open {0}",
        ["ExtStepsChromium"] = "{0} will open on its extensions page. Then:\n\n1. Turn on “Developer mode”.\n2. Click “{1}”.\n3. Paste the folder path (it is already copied):\n{2}\n\nAs soon as the extension connects, it will show as installed here.",
        ["ExtStepsFirefox"] = "Firefox only accepts extensions signed by Mozilla. Until it is published, it can be loaded temporarily (until Firefox closes):\n\n1. {0} will open on about:debugging.\n2. Click “Load Temporary Add-on…”.\n3. Choose this file (its path is already copied):\n{1}",
        ["ExtLoadUnpacked_edge"] = "Load unpacked",
        ["ExtLoadUnpacked_chrome"] = "Load unpacked",
        ["ExtConnected"] = "{0} extension connected",
        ["Cancel"] = "Cancel",

        ["Ok"] = "OK",
        ["Close"] = "Close",
        ["Back"] = "← Back",
        ["Error"] = "Error",
        ["ErrorNoActivity"] = "The uninstall screen could not be opened.",

        ["UpdateTitle"] = "Update available",
        ["UpdateBody"] = "A newer version ({0}) is available. You have {1}.\nDo you want to update?",
        ["UpdateNow"] = "Update",
        ["UpdateLater"] = "Not now",

        ["AboutTitle"] = "About",
        ["AboutVersion"] = "Version {0}",
        ["AboutContact"] = "Contact",
        ["AboutContactHint"] = "Tap to send an email",
        ["SettingsLanguage"] = "Language",
        ["AboutLanguageHint"] = "Select your preferred language",
        ["AboutDonation"] = "Support Development",
        ["AboutDonationButton"] = "Ko-fi.com - Buy me a coffee",
        ["AboutDonationHint"] = "Your support helps maintain and improve the app",
        ["AboutLegal"] = "Legal Notice",
        ["AboutLegal1"] = "This software is provided 'as is', without warranty of any kind. The user is responsible for proper use of the app and compliance with local laws.",
        ["AboutLegal2"] = "In no event shall the authors be liable for any direct, indirect, incidental or consequential damages arising from the use of this software.",
        ["AboutWarning"] = "⚠️ Use at your own risk",
        ["AboutPrivacy"] = "Privacy",
        ["AboutLicense"] = "License",
        ["AboutLicenseText"] = "This app is free software distributed under the MIT license.",
        ["EmailSubject"] = "Contact from Uninstaller",
        ["ErrorEmailNotAvailable"] = "No email app is available on this device.",
        ["ErrorEmail"] = "The email app could not be opened",
        ["BrowserNotAvailable"] = "Browser not available",
        ["LinkCopied"] = "The link was copied to the clipboard",
        ["ErrorBrowser"] = "The browser could not be opened",

        ["AppName"] = AppName,
        ["TrayOpen"] = "Open",
        ["TrayExit"] = "Exit",
        ["WindowsSection"] = "Windows",
        ["TrayOnMinimize"] = "Keep in the notification area when minimised",
        ["TrayOnMinimizeHint"] = "Click the tray icon to bring it back; right-click for Open or Exit.",
        ["StartWithWindows"] = "Start with Windows",
        ["StartWithWindowsHint"] = "Asks for the master password once when you sign in, then waits in the notification area. It asks again after you lock Windows (Win+L).",
        ["AppDescription"] = "Your passwords and two-factor codes in an encrypted vault: on this device, or in your own Google Drive or OneDrive.",
        ["AboutPrivacyText"] = "Everything you store is encrypted on your device with a key derived from your master password (Argon2id + AES-256-GCM). The vault file lives on this device or, if you choose so, in the private app folder of your own Google Drive or OneDrive. There is no server of ours, no account of ours, no analytics.",
        ["MenuVault"] = "Vault",
        ["Save"] = "Save",
        ["Delete"] = "Delete",
        ["SaveChangesQuestion"] = "Save the changes before leaving?",
        ["MenuSettings"] = "Settings",

        // Desbloqueo
        ["UnlockTitle"] = "Unlock",
        ["CreateTitle"] = "Create your vault",
        ["CreateIntro"] = "Choose a master password. It is the only key to everything you store here: nobody can recover it, not even us. Write it down somewhere safe.",
        ["MasterPassword"] = "Master password",
        ["MasterPasswordRepeat"] = "Repeat the master password",
        ["MasterPasswordShort"] = "Use at least 8 characters.",
        ["MasterPasswordMismatch"] = "The two passwords do not match.",
        ["MasterPasswordEmpty"] = "Type your master password.",
        ["TrustDevice"] = "Trust this device",
        ["TrustDeviceHint"] = "The vault opens on its own without asking for the master password. The key is kept on this device, protected by the system.",
        ["TrustDeviceConfirm"] = "Anyone who can use this device will be able to open your passwords without the master password. Turn it on only on a device that is yours alone.",
        ["TrustDeviceYes"] = "Trust",
        ["MasterPasswordWrong"] = "That is not the master password.",
        ["Unlock"] = "Unlock",
        ["Create"] = "Create",
        ["UnlockBiometric"] = "Unlock with fingerprint or face",
        ["BiometricReason"] = "Unlock your vault",
        ["OrTypePassword"] = "or type the master password",
        ["OpenExisting"] = "I already have a vault in Google Drive / OneDrive",
        ["Lock"] = "Lock",
        ["LockedByIdle"] = "Locked after {0} minutes without use.",

        // Lista
        ["SearchPlaceholder"] = "Search title, user, site or tag",
        ["AllEntries"] = "All",
        ["Favorites"] = "Favourites",
        ["NoEntries"] = "Nothing here yet.",
        ["NoEntriesHint"] = "Add your first credential with + or import from a browser or another manager in Settings.",
        ["EntriesCount"] = "{0} entries",
        ["OneEntry"] = "1 entry",
        ["Add"] = "New",
        ["KindLogin"] = "Website",
        ["KindApp"] = "Application",
        ["KindTotp"] = "Two-factor code",
        ["KindNote"] = "Secure note",
        ["SortBy"] = "Sort by",
        ["SortTitle"] = "Title",
        ["SortModified"] = "Last modified",
        ["SortCreated"] = "Created",
        ["Folders"] = "Folders",
        ["Tags"] = "Tags",
        ["NoFolder"] = "(no folder)",
        ["CopyUser"] = "Copy user name",
        ["CopyPassword"] = "Copy password",
        ["CopyCode"] = "Copy 2FA code",
        ["CopiedUser"] = "User name copied",
        ["CopiedPassword"] = "Password copied",
        ["CopiedCode"] = "Code copied",
        ["Copied"] = "Copied",
        ["ClipboardCleared"] = "Clipboard cleared",

        // Detalle
        ["Title"] = "Title",
        ["Username"] = "User name or e-mail",
        ["Password"] = "Password",
        ["Url"] = "Website (URL)",
        ["Notes"] = "Notes",
        ["Folder"] = "Folder",
        ["TagsHint"] = "Tags, separated by commas",
        ["Favorite"] = "Favourite",
        ["TotpSection"] = "Two-factor code (TOTP)",
        ["TotpSecret"] = "otpauth:// link or secret key",
        ["TotpScan"] = "Scan QR",
        ["TotpNone"] = "No two-factor configured. Paste the otpauth link, the secret key, or scan the QR the site shows.",
        ["TotpInvalid"] = "That is not a valid otpauth link or secret.",
        ["TotpSeconds"] = "{0} s",
        ["Fields"] = "Extra fields",
        ["FieldName"] = "Name",
        ["FieldValue"] = "Value",
        ["FieldHidden"] = "Hidden",
        ["AddField"] = "Add field",
        ["History"] = "Previous passwords",
        ["Generate"] = "Generate",
        ["Generator"] = "Password generator",
        ["Length"] = "Length: {0}",
        ["Uppercase"] = "Uppercase",
        ["Lowercase"] = "Lowercase",
        ["Digits"] = "Digits",
        ["Symbols"] = "Symbols",
        ["AvoidAmbiguous"] = "Avoid look-alike characters (0/O, 1/l)",
        ["Use"] = "Use",
        ["Strength0"] = "Very weak",
        ["Strength1"] = "Weak",
        ["Strength2"] = "Fair",
        ["Strength3"] = "Strong",
        ["Strength4"] = "Very strong",
        ["DeleteEntry"] = "Delete entry",
        ["EntryDeleted"] = "Entry deleted",
        ["DeleteEntryConfirm"] = "Delete “{0}”? It will be removed from all your devices.",
        ["Saved"] = "Saved",
        ["OpenSite"] = "Open website",
        ["Created"] = "Created {0}",
        ["Modified"] = "Modified {0}",
        ["TitleRequired"] = "Give the entry a title.",
        ["ShowPassword"] = "Show",
        ["HidePassword"] = "Hide",

        // Ajustes
        ["SettingsTitle"] = "Settings",
        ["StorageTitle"] = "Where the vault lives",
        ["StorageLocal"] = "Only on this device",
        ["StorageGoogle"] = "Google Drive",
        ["StorageOneDrive"] = "OneDrive",
        ["StorageHint"] = "In the cloud the file goes to the private folder of the app in your own account, encrypted with your master password. Open the same account on another device and use the same master password.",
        ["StorageNotConfigured"] = "This build has no client for {0}: fill in oauth.local.props.",
        ["SignedInAs"] = "Signed in as {0}",
        ["SignOut"] = "Sign out",
        ["SyncNow"] = "Sync now",
        ["SyncDone"] = "Synchronised: {0} entries updated here.",
        ["SyncNothing"] = "Synchronised: nothing new.",
        ["SyncFailed"] = "Could not synchronise: {0}",
        ["SignInTimeout"] = "Sign-in was not completed in time. Try again.",
        ["AutofillSaved"] = "Saved in Credentials",
        ["SignInTimeout"] = "La entrada no se completó a tiempo. Vuelve a intentarlo.",
        ["AutofillSaved"] = "Guardado en Credentials",
        ["SyncScope"] = "The account did not grant access to the app folder. Sign in again and tick that permission.",
        ["SyncPasswordNeeded"] = "The copy in the cloud was created with a different master password. Type it to merge it here (your current password stays).",
        ["SecurityTitle"] = "Security",
        ["Biometrics"] = "Unlock with fingerprint or face",
        ["BiometricsHint"] = "The key is kept in the Android Keystore and only released after your fingerprint or face is verified.",
        ["BiometricsUnavailable"] = "Not available on this device.",
        ["AutoLock"] = "Lock after inactivity",
        ["AutoLockNever"] = "Never",
        ["AutoLockMinutes"] = "{0} min",
        ["ClipboardClear"] = "Clear clipboard after",
        ["ClipboardNever"] = "Never",
        ["ClipboardSeconds"] = "{0} s",
        ["ChangeMaster"] = "Change master password",
        ["ChangeMasterDone"] = "Master password changed.",
        ["DataTitle"] = "Import and export",
        ["Import"] = "Import…",
        ["ImportHint"] = "CSV from Chrome, Edge, Firefox, Brave, Bitwarden or KeePass; JSON from Aegis, 2FAS or sOC Credentials; Google Authenticator export QR.",
        ["ImportPick"] = "Choose the file",
        ["ImportDone"] = "{0} entries imported ({1} skipped as duplicates).",
        ["ImportUnknown"] = "Could not recognise the format of that file.",
        ["ImportScanGa"] = "Scan Google Authenticator QR",
        ["ExportEncrypted"] = "Export encrypted vault",
        ["ExportPlain"] = "Export as plain JSON (unencrypted!)",
        ["ExportPlainConfirm"] = "The file will contain every password in clear text. Keep it safe and delete it when done. Continue?",
        ["Exported"] = "Exported to {0}",
        ["DangerTitle"] = "Danger zone",
        ["DeleteVault"] = "Delete this vault from the device",
        ["DeleteVaultConfirm"] = "Delete the local vault? If it is not in the cloud, everything is lost. Type DELETE to confirm.",
        ["Camera"] = "Camera",
        ["CameraDenied"] = "Without camera permission the QR cannot be scanned; paste the code instead.",
        ["ScanTitle"] = "Point at the QR code",

        // Guia de configuracion (TutorialPage)
        ["MenuTutorial"] = "Setup guide",
        ["TutStepOf"] = "Step {0} of {1}",
        ["TutNext"] = "Next",
        ["TutBack"] = "Back",
        ["TutFinish"] = "Finish",
        ["TutDone"] = "Done",
        ["TutPending"] = "Pending",
        ["TutOptional"] = "Optional",
        ["TutOpenSettings"] = "Open Settings",
        ["TutWelcomeTitle"] = "Let's get everything ready",
        ["TutWelcomeWindows"] = "In a few steps, sOC Credentials will be working on this PC: always at hand next to the clock, filling in passwords in desktop apps and, with its extension, in every browser you have installed.\n\nEach step has a button that does it for you and marks itself as done. You can come back to this guide at any time from the menu.",
        ["TutWelcomeAndroid"] = "In a few steps, sOC Credentials will be filling in your passwords in the apps and browsers on this device.\n\nEach step has a button that takes you to the system or browser screen where it is changed; when you come back, the step marks itself. You can come back to this guide at any time from the menu.",
        ["TutTrayTitle"] = "Always at hand",
        ["TutTrayBody"] = "sOC Credentials starts with Windows and, when minimised, stays next to the clock instead of closing. That way the browser extension and autofill always find it, and the master password is asked only once per session.",
        ["TutTrayAction"] = "Start with Windows and stay next to the clock",
        ["TutDesktopTitle"] = "Passwords in apps",
        ["TutDesktopBody"] = "When you enter a password field in any desktop app, sOC Credentials shows the matching entries right next to it so you can fill it in with one click.",
        ["TutDesktopAction"] = "Turn on desktop autofill",
        ["TutNoBrowsersTitle"] = "No supported browsers",
        ["TutNoBrowsersBody"] = "Edge, Chrome or Firefox were not found on this PC (or this installation does not include the extension). If you install one, come back to this guide and its step will appear.",
        ["TutExtTitle"] = "Extension for {0}",
        ["TutExtBodyChromium"] = "The extension fills in and saves passwords in {0} and gives you each site's verification code (TOTP).\n\nThe button prepares everything, copies the folder path and opens the {0} extensions page. There: turn on “Developer mode”, click “Load unpacked” and paste the path. As soon as the extension connects, this step is marked as done.",
        ["TutExtBodyFirefox"] = "The extension fills in and saves passwords in {0} and gives you each site's verification code (TOTP).\n\nFirefox only accepts extensions signed by Mozilla: until it is published, it is loaded as a temporary add-on and has to be loaded again every time Firefox opens. The button copies the file path and opens about:debugging: click “Load Temporary Add-on…” and choose that file.",
        ["TutExtAction"] = "Install in {0}",
        ["TutBrowserPmTitle"] = "Turn off the browser's manager",
        ["TutBrowserPmWindows"] = "If the browser keeps saving passwords on its own, it will offer both at once. Click the sOC Credentials icon in the browser toolbar and, under “Browser options”, turn off its password manager. The first time you open the extension it already asks you.",
        ["TutAutofillTitle"] = "Autofill service",
        ["TutAutofillBody"] = "Android lets only one manager fill in passwords in apps. Choose sOC Credentials on the screen that opens; the previous one gets disconnected.",
        ["TutPreferredTitle"] = "Preferred password service",
        ["TutPreferredBody"] = "Since Android 14, browsers follow the “preferred” password service, not the autofill one. On the screen that opens, choose sOC Credentials as preferred (or, if it is not listed, remove Google) so the browser offers your entries.",
        ["TutBrowserTitle"] = "Passwords in {0}",
        ["TutBrowserChromium"] = "{0} has its own password manager. In its Settings: “Passwords” → turn off saving and filling passwords; and under “Autofill services” (or “Autofill”) choose “Autofill using another service”. Then come back here.",
        ["TutBrowserFirefox"] = "{0} has its own manager. In its Settings → “Passwords”: turn off “Save passwords” and turn on “Autofill in other apps and services” if it appears. Then come back here.",
        ["TutBrowserAction"] = "Open {0}",
        ["TutBioTitle"] = "Sign in with your fingerprint",
        ["TutBioBody"] = "To avoid typing the master password every time, turn on fingerprint (or device unlock) in Settings › Security.",
        ["TutCloudTitle"] = "Your passwords on all your devices",
        ["TutCloudBody"] = "If you keep the vault in Google Drive or OneDrive, encrypted with your master password, you will have the same one on your PC and your phone. It is chosen in Settings › Storage. It is optional: without the cloud, the vault stays on this device only.",
        ["TutEndTitle"] = "All set!",
        ["TutEndBody"] = "sOC Credentials is set up. If any step is still pending, you can come back to this guide at any time from the menu.",
    };

    private static readonly Dictionary<string, string> Spanish = new()
    {
        ["Company"] = "Socratic",

        ["MenuHome"] = "Inicio",
        ["About"] = "Acerca de",




        ["Continue"] = "Continuar",
        ["NotNow"] = "Ahora no",
        ["ExtSection"] = "Extensiones del navegador",
        ["AutofillSection"] = "Autocompletar",
        ["AutofillIsOurs"] = "sOC Credentials rellena las contraseñas en este dispositivo.",
        ["AutofillIsOther"] = "Otro gestor de contraseñas (o ninguno) rellena las contraseñas en este dispositivo. Android solo admite uno: al elegir sOC Credentials se desconecta el otro.",
        ["AutofillUseThis"] = "Usar sOC Credentials para autocompletar",
        ["AutofillChange"] = "Cambiar el servicio de autocompletar",
        ["AutofillPreferredHint"] = "En los navegadores (Edge, Chrome…) manda el servicio preferido de contraseñas del sistema, no el de autocompletar: mientras ahí esté Google, el navegador ofrecerá Google.",
        ["AutofillPreferredOpen"] = "Servicio preferido de contraseñas",
        ["AutofillPreferredNoScreen"] = "Este dispositivo no tiene esa pantalla.",
        ["AutofillBrowsersHint"] = "Cada navegador tiene además su propio gestor de contraseñas, y dentro de él gana ese. Se apaga en los ajustes del propio navegador.",
        ["AutofillBrowsersOpen"] = "Abrir los ajustes del navegador",
        ["AutofillBrowserSteps"] = "En {0}: Ajustes › Contraseñas (o Autocompletar) → apaga guardar y rellenar contraseñas y, si hay opción de usar otro servicio, elige sOC Credentials. Después vuelve aquí.",
        ["AutofillOfferTitle"] = "Otro gestor de contraseñas (o ninguno) está rellenando tus contraseñas. ¿Usar sOC Credentials en su lugar? El otro queda desconectado.",
        ["AutofillAskOnUnlock"] = "Proponerlo al desbloquear",
        ["AutofillAskOnUnlockHint"] = "Mientras otro gestor sea el servicio de autocompletar.",
        ["DesktopAutofill"] = "Rellenar en las aplicaciones de Windows",
        ["DesktopAutofillHint"] = "Cuando un campo de contraseña de un programa recibe el foco, aparece a su lado una lista con las entradas que casan (las webs van por la extensión del navegador).",
        ["DesktopFillHint"] = "Pulsa una para teclear el usuario y la contraseña.",
        ["ExtSectionHint"] = "Rellena usuarios, contraseñas y códigos de segundo factor en Edge, Chrome y Firefox, y ofrece guardar los nuevos. Habla con esta aplicación: nada sale de tu PC.",
        ["ExtInstalled"] = "Instalada",
        ["ExtNotInstalled"] = "No instalada",
        ["ExtInstallButton"] = "Instalar…",
        ["ExtAskOnUnlock"] = "Ofrecer instalarla al desbloquear",
        ["ExtAskOnUnlockHint"] = "Solo para los navegadores de este PC que aún no la tengan.",
        ["ExtOfferTitle"] = "{0}: la extensión de sOC Credentials no está instalada. ¿Instalarla ahora?",
        ["ExtInstallNow"] = "Instalar ahora",
        ["ExtDontAsk"] = "No volver a preguntar",
        ["ExtInstallIn"] = "Instalar en {0}",
        ["ExtOpenBrowser"] = "Abrir {0}",
        ["ExtStepsChromium"] = "Se abrirá {0} en su página de extensiones. Después:\n\n1. Activa el «Modo de desarrollador».\n2. Pulsa «{1}».\n3. Pega la ruta de la carpeta (ya está copiada):\n{2}\n\nEn cuanto la extensión conecte, aquí saldrá como instalada.",
        ["ExtStepsFirefox"] = "Firefox solo admite extensiones firmadas por Mozilla. Hasta que se publique, se puede cargar temporalmente (hasta cerrar Firefox):\n\n1. Se abrirá {0} en about:debugging.\n2. Pulsa «Cargar complemento temporal…».\n3. Elige este fichero (la ruta ya está copiada):\n{1}",
        ["ExtLoadUnpacked_edge"] = "Cargar desempaquetada",
        ["ExtLoadUnpacked_chrome"] = "Cargar descomprimida",
        ["ExtConnected"] = "Extensión de {0} conectada",
        ["Cancel"] = "Cancelar",

        ["Ok"] = "Aceptar",
        ["Close"] = "Cerrar",
        ["Back"] = "← Volver",
        ["Error"] = "Error",
        ["ErrorNoActivity"] = "No se ha podido abrir la pantalla de desinstalación.",

        ["UpdateTitle"] = "Actualización disponible",
        ["UpdateBody"] = "Hay una versión más reciente ({0}). Tienes la {1}.\n¿Quieres actualizar?",
        ["UpdateNow"] = "Actualizar",
        ["UpdateLater"] = "Ahora no",

        ["AboutTitle"] = "Acerca de",
        ["AboutVersion"] = "Versión {0}",
        ["AboutContact"] = "Contacto",
        ["AboutContactHint"] = "Toca para enviar un correo electrónico",
        ["SettingsLanguage"] = "Idioma",
        ["AboutLanguageHint"] = "Selecciona tu idioma preferido",
        ["AboutDonation"] = "Apoya el Desarrollo",
        ["AboutDonationButton"] = "Ko-fi.com - Invítame un café",
        ["AboutDonationHint"] = "Tu apoyo ayuda a mantener y mejorar la aplicación",
        ["AboutLegal"] = "Aviso Legal",
        ["AboutLegal1"] = "Este software se proporciona «tal cual», sin garantías de ningún tipo. El usuario es responsable del uso adecuado de la aplicación y del cumplimiento de las leyes locales.",
        ["AboutLegal2"] = "En ningún caso los autores serán responsables de daños directos, indirectos, incidentales o consecuentes que resulten del uso de este software.",
        ["AboutWarning"] = "⚠️ Uso bajo su propio riesgo",
        ["AboutPrivacy"] = "Privacidad",
        ["AboutLicense"] = "Licencia",
        ["AboutLicenseText"] = "Esta aplicación es software libre distribuido bajo licencia MIT.",
        ["EmailSubject"] = "Contacto desde Desinstalador",
        ["ErrorEmailNotAvailable"] = "No hay ninguna aplicación de correo disponible en este dispositivo.",
        ["ErrorEmail"] = "No se ha podido abrir la aplicación de correo",
        ["BrowserNotAvailable"] = "Navegador no disponible",
        ["LinkCopied"] = "El enlace se ha copiado al portapapeles",
        ["ErrorBrowser"] = "No se ha podido abrir el navegador",

        ["AppName"] = AppName,
        ["TrayOpen"] = "Abrir",
        ["TrayExit"] = "Salir",
        ["WindowsSection"] = "Windows",
        ["TrayOnMinimize"] = "Quedarse en el área de notificación al minimizar",
        ["TrayOnMinimizeHint"] = "Clic en el icono de la bandeja para volver; botón derecho para Abrir o Salir.",
        ["StartWithWindows"] = "Arrancar con Windows",
        ["StartWithWindowsHint"] = "Pide la contraseña maestra una vez al iniciar sesión y se queda en el área de notificación. Vuelve a pedirla tras bloquear Windows (Win+L).",
        ["AppDescription"] = "Tus contraseñas y códigos de segundo factor en una bóveda cifrada: en este dispositivo, o en tu propio Google Drive u OneDrive.",
        ["AboutPrivacyText"] = "Todo lo que guardas se cifra en tu dispositivo con una clave derivada de tu contraseña maestra (Argon2id + AES-256-GCM). El fichero de la bóveda vive en este dispositivo o, si tú lo eliges, en la carpeta privada de la aplicación de tu propio Google Drive u OneDrive. No hay servidor nuestro, ni cuenta nuestra, ni analítica.",
        ["MenuVault"] = "Bóveda",
        ["Save"] = "Guardar",
        ["Delete"] = "Borrar",
        ["SaveChangesQuestion"] = "¿Guardar los cambios antes de salir?",
        ["MenuSettings"] = "Ajustes",

        // Desbloqueo
        ["UnlockTitle"] = "Desbloquear",
        ["CreateTitle"] = "Crea tu bóveda",
        ["CreateIntro"] = "Elige una contraseña maestra. Es la única llave de todo lo que guardes aquí: nadie puede recuperarla, nosotros tampoco. Apúntala en un sitio seguro.",
        ["MasterPassword"] = "Contraseña maestra",
        ["MasterPasswordRepeat"] = "Repite la contraseña maestra",
        ["MasterPasswordShort"] = "Usa al menos 8 caracteres.",
        ["MasterPasswordMismatch"] = "Las dos contraseñas no coinciden.",
        ["MasterPasswordEmpty"] = "Escribe tu contraseña maestra.",
        ["TrustDevice"] = "Confiar en este dispositivo",
        ["TrustDeviceHint"] = "La bóveda se abre sola, sin pedir la contraseña maestra. La clave queda guardada en este dispositivo, protegida por el sistema.",
        ["TrustDeviceConfirm"] = "Cualquiera que pueda usar este dispositivo podrá abrir tus contraseñas sin la contraseña maestra. Actívalo solo en un dispositivo que sea solo tuyo.",
        ["TrustDeviceYes"] = "Confiar",
        ["MasterPasswordWrong"] = "Esa no es la contraseña maestra.",
        ["Unlock"] = "Desbloquear",
        ["Create"] = "Crear",
        ["UnlockBiometric"] = "Desbloquear con huella o cara",
        ["BiometricReason"] = "Desbloquear tu bóveda",
        ["OrTypePassword"] = "o escribe la contraseña maestra",
        ["OpenExisting"] = "Ya tengo una bóveda en Google Drive / OneDrive",
        ["Lock"] = "Bloquear",
        ["LockedByIdle"] = "Bloqueada tras {0} minutos sin uso.",

        // Lista
        ["SearchPlaceholder"] = "Buscar por título, usuario, sitio o etiqueta",
        ["AllEntries"] = "Todas",
        ["Favorites"] = "Favoritas",
        ["NoEntries"] = "Todavía no hay nada.",
        ["NoEntriesHint"] = "Añade tu primera credencial con + o importa desde un navegador u otro gestor en Ajustes.",
        ["EntriesCount"] = "{0} entradas",
        ["OneEntry"] = "1 entrada",
        ["Add"] = "Nueva",
        ["KindLogin"] = "Sitio web",
        ["KindApp"] = "Aplicación",
        ["KindTotp"] = "Código de segundo factor",
        ["KindNote"] = "Nota segura",
        ["SortBy"] = "Ordenar por",
        ["SortTitle"] = "Título",
        ["SortModified"] = "Última modificación",
        ["SortCreated"] = "Creación",
        ["Folders"] = "Carpetas",
        ["Tags"] = "Etiquetas",
        ["NoFolder"] = "(sin carpeta)",
        ["CopyUser"] = "Copiar el usuario",
        ["CopyPassword"] = "Copiar la contraseña",
        ["CopyCode"] = "Copiar el código de segundo factor",
        ["CopiedUser"] = "Usuario copiado",
        ["CopiedPassword"] = "Contraseña copiada",
        ["CopiedCode"] = "Código copiado",
        ["Copied"] = "Copiado",
        ["ClipboardCleared"] = "Portapapeles vaciado",

        // Detalle
        ["Title"] = "Título",
        ["Username"] = "Usuario o correo",
        ["Password"] = "Contraseña",
        ["Url"] = "Sitio web (URL)",
        ["Notes"] = "Notas",
        ["Folder"] = "Carpeta",
        ["TagsHint"] = "Etiquetas, separadas por comas",
        ["Favorite"] = "Favorita",
        ["TotpSection"] = "Código de segundo factor (TOTP)",
        ["TotpSecret"] = "Enlace otpauth:// o clave secreta",
        ["TotpScan"] = "Escanear QR",
        ["TotpNone"] = "Sin segundo factor. Pega el enlace otpauth, la clave secreta, o escanea el QR que enseña el sitio.",
        ["TotpInvalid"] = "Eso no es un enlace otpauth ni una clave válidos.",
        ["TotpSeconds"] = "{0} s",
        ["Fields"] = "Campos extra",
        ["FieldName"] = "Nombre",
        ["FieldValue"] = "Valor",
        ["FieldHidden"] = "Oculto",
        ["AddField"] = "Añadir campo",
        ["History"] = "Contraseñas anteriores",
        ["Generate"] = "Generar",
        ["Generator"] = "Generador de contraseñas",
        ["Length"] = "Longitud: {0}",
        ["Uppercase"] = "Mayúsculas",
        ["Lowercase"] = "Minúsculas",
        ["Digits"] = "Números",
        ["Symbols"] = "Símbolos",
        ["AvoidAmbiguous"] = "Evitar caracteres que se confunden (0/O, 1/l)",
        ["Use"] = "Usar",
        ["Strength0"] = "Muy débil",
        ["Strength1"] = "Débil",
        ["Strength2"] = "Aceptable",
        ["Strength3"] = "Fuerte",
        ["Strength4"] = "Muy fuerte",
        ["DeleteEntry"] = "Borrar entrada",
        ["EntryDeleted"] = "Entrada borrada",
        ["DeleteEntryConfirm"] = "¿Borrar «{0}»? Desaparecerá de todos tus dispositivos.",
        ["Saved"] = "Guardado",
        ["OpenSite"] = "Abrir el sitio",
        ["Created"] = "Creada el {0}",
        ["Modified"] = "Modificada el {0}",
        ["TitleRequired"] = "Ponle un título a la entrada.",
        ["ShowPassword"] = "Ver",
        ["HidePassword"] = "Ocultar",

        // Ajustes
        ["SettingsTitle"] = "Ajustes",
        ["StorageTitle"] = "Dónde vive la bóveda",
        ["StorageLocal"] = "Solo en este dispositivo",
        ["StorageGoogle"] = "Google Drive",
        ["StorageOneDrive"] = "OneDrive",
        ["StorageHint"] = "En la nube el fichero va a la carpeta privada de la aplicación en tu propia cuenta, cifrado con tu contraseña maestra. Abre la misma cuenta en otro dispositivo y usa la misma contraseña maestra.",
        ["StorageNotConfigured"] = "Esta compilación no lleva cliente de {0}: hay que rellenar oauth.local.props.",
        ["SignedInAs"] = "Sesión iniciada como {0}",
        ["SignOut"] = "Cerrar sesión",
        ["SyncNow"] = "Sincronizar ahora",
        ["SyncDone"] = "Sincronizado: {0} entradas actualizadas aquí.",
        ["SyncNothing"] = "Sincronizado: nada nuevo.",
        ["SyncFailed"] = "No se ha podido sincronizar: {0}",
        ["SyncScope"] = "La cuenta no concedió el acceso a la carpeta de la aplicación. Vuelve a entrar y marca ese permiso.",
        ["SyncPasswordNeeded"] = "La copia de la nube se creó con otra contraseña maestra. Escríbela para mezclarla aquí (tu contraseña actual se mantiene).",
        ["SecurityTitle"] = "Seguridad",
        ["Biometrics"] = "Desbloquear con huella o cara",
        ["BiometricsHint"] = "La clave queda en el Keystore de Android y solo se libera cuando se verifica tu huella o tu cara.",
        ["BiometricsUnavailable"] = "No disponible en este dispositivo.",
        ["AutoLock"] = "Bloquear tras inactividad",
        ["AutoLockNever"] = "Nunca",
        ["AutoLockMinutes"] = "{0} min",
        ["ClipboardClear"] = "Vaciar el portapapeles a los",
        ["ClipboardNever"] = "Nunca",
        ["ClipboardSeconds"] = "{0} s",
        ["ChangeMaster"] = "Cambiar la contraseña maestra",
        ["ChangeMasterDone"] = "Contraseña maestra cambiada.",
        ["DataTitle"] = "Importar y exportar",
        ["Import"] = "Importar…",
        ["ImportHint"] = "CSV de Chrome, Edge, Firefox, Brave, Bitwarden o KeePass; JSON de Aegis, 2FAS o sOC Credentials; QR de exportación de Google Authenticator.",
        ["ImportPick"] = "Elige el fichero",
        ["ImportDone"] = "{0} entradas importadas ({1} saltadas por repetidas).",
        ["ImportUnknown"] = "No se reconoce el formato de ese fichero.",
        ["ImportScanGa"] = "Escanear QR de Google Authenticator",
        ["ExportEncrypted"] = "Exportar la bóveda cifrada",
        ["ExportPlain"] = "Exportar a JSON en claro (¡sin cifrar!)",
        ["ExportPlainConfirm"] = "El fichero llevará todas las contraseñas legibles. Guárdalo a buen recaudo y bórralo al acabar. ¿Seguir?",
        ["Exported"] = "Exportado a {0}",
        ["DangerTitle"] = "Zona peligrosa",
        ["DeleteVault"] = "Borrar esta bóveda del dispositivo",
        ["DeleteVaultConfirm"] = "¿Borrar la bóveda local? Si no está en la nube, se pierde todo. Escribe BORRAR para confirmar.",
        ["Camera"] = "Cámara",
        ["CameraDenied"] = "Sin permiso de cámara no se puede escanear el QR; pega el código en su lugar.",
        ["ScanTitle"] = "Apunta al código QR",

        // Guia de configuracion (TutorialPage)
        ["MenuTutorial"] = "Guía de configuración",
        ["TutStepOf"] = "Paso {0} de {1}",
        ["TutNext"] = "Siguiente",
        ["TutBack"] = "Anterior",
        ["TutFinish"] = "Terminar",
        ["TutDone"] = "Hecho",
        ["TutPending"] = "Pendiente",
        ["TutOptional"] = "Opcional",
        ["TutOpenSettings"] = "Abrir Ajustes",
        ["TutWelcomeTitle"] = "Vamos a dejarlo todo listo",
        ["TutWelcomeWindows"] = "En unos pasos, sOC Credentials quedará funcionando en este PC: siempre a mano junto al reloj, rellenando contraseñas en las aplicaciones de escritorio y, con su extensión, en cada navegador que tengas instalado.\n\nCada paso tiene un botón que lo hace por ti y se marca como hecho solo. Puedes volver a esta guía cuando quieras desde el menú.",
        ["TutWelcomeAndroid"] = "En unos pasos, sOC Credentials quedará rellenando tus contraseñas en las aplicaciones y en los navegadores de este dispositivo.\n\nCada paso tiene un botón que te lleva a la pantalla del sistema o del navegador donde se cambia; al volver, el paso se marca solo. Puedes volver a esta guía cuando quieras desde el menú.",
        ["TutTrayTitle"] = "Siempre a mano",
        ["TutTrayBody"] = "sOC Credentials arranca con Windows y, al minimizarla, se queda junto al reloj en vez de cerrarse. Así la extensión del navegador y el autocompletar siempre la encuentran, y la contraseña maestra se pide una sola vez por sesión.",
        ["TutTrayAction"] = "Arrancar con Windows y quedarse junto al reloj",
        ["TutDesktopTitle"] = "Contraseñas en las aplicaciones",
        ["TutDesktopBody"] = "Al entrar en un campo de contraseña de cualquier aplicación de escritorio, sOC Credentials enseña al lado la lista de entradas que le encajan para rellenarla con un clic.",
        ["TutDesktopAction"] = "Activar el autocompletar de escritorio",
        ["TutNoBrowsersTitle"] = "Sin navegadores compatibles",
        ["TutNoBrowsersBody"] = "No se ha encontrado Edge, Chrome ni Firefox en este PC (o esta instalación no trae la extensión). Si instalas uno, vuelve a esta guía y aparecerá su paso.",
        ["TutExtTitle"] = "Extensión para {0}",
        ["TutExtBodyChromium"] = "La extensión rellena y guarda contraseñas en {0} y te da el código de verificación (TOTP) de cada web.\n\nAl pulsar el botón se prepara todo, se copia la ruta de la carpeta y se abre la página de extensiones de {0}. Allí: activa el «Modo de desarrollador», pulsa «Cargar desempaquetada» y pega la ruta. En cuanto la extensión conecte, este paso se marca como hecho.",
        ["TutExtBodyFirefox"] = "La extensión rellena y guarda contraseñas en {0} y te da el código de verificación (TOTP) de cada web.\n\nFirefox solo admite extensiones firmadas por Mozilla: hasta que se publique, se carga como complemento temporal y hay que volver a cargarla cada vez que se abre Firefox. Al pulsar el botón se copia la ruta del fichero y se abre about:debugging: pulsa «Cargar complemento temporal…» y elige ese fichero.",
        ["TutExtAction"] = "Instalar en {0}",
        ["TutBrowserPmTitle"] = "Apaga el gestor del navegador",
        ["TutBrowserPmWindows"] = "Si el navegador sigue guardando contraseñas por su cuenta, te ofrecerá las dos cosas a la vez. Pulsa el icono de sOC Credentials en la barra del navegador y, en «Opciones del navegador», desactiva su gestor de contraseñas. La primera vez que abras la extensión ya te lo pregunta.",
        ["TutAutofillTitle"] = "Servicio de autocompletar",
        ["TutAutofillBody"] = "Android deja que un solo gestor rellene las contraseñas de las aplicaciones. Elige sOC Credentials en la pantalla que se abre; el que hubiera antes queda desconectado.",
        ["TutPreferredTitle"] = "Servicio preferido de contraseñas",
        ["TutPreferredBody"] = "Desde Android 14, en los navegadores manda el «servicio preferido» de contraseñas y no el de autocompletar. En la pantalla que se abre, elige sOC Credentials como preferido (o, si no aparece, quita Google) para que el navegador te ofrezca tus entradas.",
        ["TutBrowserTitle"] = "Contraseñas en {0}",
        ["TutBrowserChromium"] = "{0} trae su propio gestor de contraseñas. En sus Ajustes: «Contraseñas» → apaga guardar y rellenar contraseñas; y en «Servicios de autocompletar» (o «Autocompletar») elige «Autocompletar con otro servicio». Después vuelve aquí.",
        ["TutBrowserFirefox"] = "{0} trae su propio gestor. En sus Ajustes → «Contraseñas»: apaga «Guardar contraseñas» y activa «Autocompletar en otras aplicaciones y servicios» si aparece. Después vuelve aquí.",
        ["TutBrowserAction"] = "Abrir {0}",
        ["TutBioTitle"] = "Entrar con la huella",
        ["TutBioBody"] = "Para no teclear la contraseña maestra cada vez, activa la huella (o el desbloqueo del dispositivo) en Ajustes › Seguridad.",
        ["TutCloudTitle"] = "Tus contraseñas en todos tus dispositivos",
        ["TutCloudBody"] = "Si guardas la bóveda en Google Drive o en OneDrive, cifrada con tu contraseña maestra, la tendrás igual en el PC y en el móvil. Se elige en Ajustes › Almacenamiento. Es opcional: sin nube, la bóveda se queda solo en este dispositivo.",
        ["TutEndTitle"] = "¡Todo listo!",
        ["TutEndBody"] = "sOC Credentials ya está configurado. Si algún paso ha quedado pendiente, puedes volver a esta guía cuando quieras desde el menú.",
    };
}
