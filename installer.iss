[Setup]
AppId={{6beae8ff-1bc2-44c6-8e94-b9270fe6ff22}
AppName=Gallery
AppVersion=0.1
AppPublisher=Mohamed Matar
DefaultDirName={autopf}\Gallery
DefaultGroupName=Gallery
OutputDir=installer
OutputBaseFilename=Gallery_Setup_v0.1
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Gallery.exe
SetupIconFile=Resources\AppIcon\gallery.ico

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Gallery"; Filename: "{app}\Gallery.exe"
Name: "{group}\Uninstall Gallery"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Gallery"; Filename: "{app}\Gallery.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Gallery.exe"; Description: "Launch Gallery after installation"; Flags: nowait postinstall skipifsilent
