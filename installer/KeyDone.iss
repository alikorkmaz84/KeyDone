; KeyDone kurulum betiği (Inno Setup 6).
; Paketle.ps1 çağırır: ISCC /DAppVersion=0.4.0 /DSourceDir=...\yayin /DOutputDir=...\dagitim KeyDone.iss
; Yönetici izni istemez; varsayılan olarak yalnızca geçerli kullanıcı için kurar.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\yayin"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\dagitim"
#endif

[Setup]
AppId={{6F2C8B1E-4D7A-4C21-9E3B-8A51D0C47E92}
AppName=KeyDone
AppVersion={#AppVersion}
AppVerName=KeyDone {#AppVersion}
AppPublisher=Ali Korkmaz
AppPublisherURL=https://github.com/alikorkmaz84/KeyDone
AppSupportURL=https://github.com/alikorkmaz84/KeyDone/issues
DefaultDirName={autopf}\KeyDone
DefaultGroupName=KeyDone
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=KeyDone-{#AppVersion}-Kurulum
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=KeyDone
UninstallDisplayIcon={app}\KeyDone.exe
LicenseFile=..\LICENSE

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek kısayollar:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\KeyDone"; Filename: "{app}\KeyDone.exe"
Name: "{autodesktop}\KeyDone"; Filename: "{app}\KeyDone.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KeyDone.exe"; Description: "KeyDone'u şimdi başlat"; Flags: nowait postinstall skipifsilent
