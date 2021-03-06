
function pause() {
    Write-Host "Press any key to continue ..."
    cmd /c pause | out-null
}

function buildApiDocs($srcDir) {
    Write-Host "Building YuiDoc..."
    $targetDir = "$srcDir\Breeze.Client\Scripts\IBlade"
    cd $targetDir
    $yuiDocExpr = 'yuidoc -t "$srcDir\apidoc-theme\breeze" .'
    $output = Invoke-Expression $yuiDocExpr
    $output | out-string
    Write-Host "Building YuiDoc complete"
}    

function buildIntellisense($srcDir) {
    Write-Host "Building Intellisense..."
    $targetDir = "$srcDir\Breeze.Intellisense"
    cd $targetDir
    $nodeExpr = 'node server.js'
    $output = Invoke-Expression $nodeExpr
    $output | out-string
    Write-Host "Building Intellisense done"
}    

$srcDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
buildApiDocs $srcDir 
buildIntellisense $srcDir
pause
