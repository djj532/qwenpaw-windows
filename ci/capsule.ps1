param(
  [ValidateSet('decrypt','encrypt')][string]$Mode,
  [string]$InputFile,
  [string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$keyText = [string]$env:QP_CI_CAPSULE_KEY
if ([string]::IsNullOrWhiteSpace($keyText)) {
  $rsa = [Security.Cryptography.RSA]::Create()
  try {
    $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $PWD '.private/transport-private.pem')))
    $wrapped = [IO.File]::ReadAllBytes((Join-Path $PWD 'ci/inbox/transport-key.wrap'))
    $transportKey = $rsa.Decrypt($wrapped, [Security.Cryptography.RSAEncryptionPadding]::OaepSHA256)
    $keyText = [Convert]::ToBase64String($transportKey)
    [Array]::Clear($transportKey, 0, $transportKey.Length)
  } finally { $rsa.Dispose() }
}
Write-Output "::add-mask::$keyText"
[byte[]]$key = [Convert]::FromBase64String($keyText)
if ($key.Length -ne 32) { throw 'Invalid transport key' }
[byte[]]$magic = [Text.Encoding]::ASCII.GetBytes('QPCICAP1')
if (Test-Path -LiteralPath $OutputFile) { throw 'Refusing capsule output overwrite' }
$aes = [Security.Cryptography.AesGcm]::new($key, 16)
try {
  [byte[]]$data = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InputFile))
  if ($Mode -eq 'decrypt') {
    if ($data.Length -lt 36) { throw 'Invalid encrypted archive' }
    if ([Convert]::ToHexString($data[0..7]) -ne [Convert]::ToHexString($magic)) { throw 'Invalid archive header' }
    [byte[]]$nonce = [byte[]]::new(12)
    [byte[]]$tag = [byte[]]::new(16)
    [byte[]]$ciphertext = [byte[]]::new($data.Length-36)
    [Array]::Copy($data, 8, $nonce, 0, 12)
    [Array]::Copy($data, $data.Length-16, $tag, 0, 16)
    [Array]::Copy($data, 20, $ciphertext, 0, $ciphertext.Length)
    [byte[]]$plain = [byte[]]::new($ciphertext.Length)
    $aes.Decrypt($nonce, $ciphertext, $tag, $plain, $magic)
    [IO.File]::WriteAllBytes($OutputFile, $plain)
  } else {
    [byte[]]$nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    [byte[]]$ciphertext = [byte[]]::new($data.Length)
    [byte[]]$tag = [byte[]]::new(16)
    $aes.Encrypt($nonce, $data, $ciphertext, $tag, $magic)
    $outputStream = [IO.File]::Open($OutputFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
      $outputStream.Write($magic, 0, $magic.Length)
      $outputStream.Write($nonce, 0, $nonce.Length)
      $outputStream.Write($ciphertext, 0, $ciphertext.Length)
      $outputStream.Write($tag, 0, $tag.Length)
    } finally { $outputStream.Dispose() }
  }
  Write-Output "CAPSULE_AUTHENTICATED_$Mode"
} finally {
  $aes.Dispose()
  [Array]::Clear($key, 0, $key.Length)
}
