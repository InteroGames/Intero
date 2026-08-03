# Unity 6.3 LTS (6000.3.21f1) 이전 스크립트
#
# 실행 전 반드시:
#   1. Unity Hub에서 6000.3.21f1 설치 (모듈: Windows Build Support (IL2CPP))
#   2. 열려 있는 Unity 에디터를 전부 종료
#
# 실행:  powershell -ExecutionPolicy Bypass -File migrate-to-6.3.ps1
#
# 하는 일: 버전 고정 + 에디터 버전에 종속된 생성물 삭제.
# 씬·머티리얼·폰트 에셋은 전부 Setup Scene이 코드로 다시 만들기 때문에 지워도 잃는 것이 없다.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = '6000.3.21f1'
$editor = "C:\Program Files\Unity\Hub\Editor\$target\Editor\Unity.exe"

if (-not (Test-Path $editor)) {
    Write-Host "[중단] Unity $target 이 설치돼 있지 않습니다." -ForegroundColor Red
    Write-Host "       Unity Hub > Installs > Install Editor 에서 $target 을 먼저 설치하세요."
    exit 1
}

if (Get-Process Unity -ErrorAction SilentlyContinue) {
    Write-Host "[중단] Unity 에디터가 실행 중입니다. 전부 종료한 뒤 다시 실행하세요." -ForegroundColor Red
    exit 1
}

Write-Host "1/3  ProjectVersion.txt 를 $target 로 고정" -ForegroundColor Cyan
@"
m_EditorVersion: $target
m_EditorVersionWithRevision: $target
"@ | Set-Content -Path (Join-Path $root 'ProjectSettings\ProjectVersion.txt') -Encoding utf8

Write-Host "2/3  이전 버전이 만든 캐시 삭제 (Library, Temp, obj)" -ForegroundColor Cyan
foreach ($d in 'Library', 'Temp', 'obj') {
    $p = Join-Path $root $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Write-Host "3/3  코드로 재생성되는 산출물 삭제 (씬, 머티리얼, 폰트 에셋)" -ForegroundColor Cyan
foreach ($p in 'Assets\Scenes', 'Assets\Materials', 'Assets\Fonts\Malgun SDF.asset', 'Assets\Fonts\Malgun SDF.asset.meta') {
    $full = Join-Path $root $p
    if (Test-Path $full) { Remove-Item $full -Recurse -Force }
}

Write-Host ""
Write-Host "완료. 다음 순서로 진행하세요:" -ForegroundColor Green
Write-Host "  1. Unity Hub 에서 이 프로젝트를 $target 로 연다 (첫 임포트 수 분 소요)"
Write-Host "  2. Tools > Interrogation > Setup Scene"
Write-Host "  3. Play"
Write-Host ""
Write-Host "그 뒤 재생성된 Assets/Scenes, Assets/Materials, 폰트 에셋을 커밋하면 팀원도 같은 상태가 됩니다."
