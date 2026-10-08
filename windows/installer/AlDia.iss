; Instalador de Al Día para Windows (Inno Setup 6).
; Se compila en GitHub Actions: iscc /DVersion=0.1.0 /DOrigen=..\publish\win-x64 AlDia.iss

#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef Origen
  #define Origen "..\publish\win-x64"
#endif

[Setup]
AppId={{CBA54B9E-E35B-4846-98EA-4291EE5AF506}
AppName=Al Día
AppVersion={#Version}
AppVerName=Al Día {#Version}
AppPublisher=juanitreque
AppPublisherURL=https://github.com/juanitreque/al-dia
AppSupportURL=https://github.com/juanitreque/al-dia/issues
DefaultDirName={autopf}\Al Dia
DefaultGroupName=Al Día
DisableProgramGroupPage=yes
; Se instala solo para el usuario actual: no pide contraseña de administrador
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=salida
OutputBaseFilename=AlDia-Windows-{#Version}-instalador
SetupIconFile=..\src\AlDia\Recursos\AlDia.ico
UninstallDisplayIcon={app}\AlDia.exe
LicenseFile=..\..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "escritorio"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Origen}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\Al Día"; Filename: "{app}\AlDia.exe"
Name: "{group}\Al Día (demostración)"; Filename: "{app}\AlDia.exe"; Parameters: "--demo"
Name: "{autodesktop}\Al Día"; Filename: "{app}\AlDia.exe"; Tasks: escritorio

[Run]
Filename: "{app}\AlDia.exe"; Description: "{cm:LaunchProgram,Al Día}"; Flags: nowait postinstall skipifsilent

; Los datos (%APPDATA%\AlDia) y las facturas (Documentos\Al Día) no se borran al desinstalar.
