param([string]$ResourceSource='')
$ErrorActionPreference='Stop'
if(!$ResourceSource){$ResourceSource=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.windowsappsdk.winui/2.3.9/lib/native/Microsoft.UI/Themes/generic.xaml'}
$source=[xml][IO.File]::ReadAllText((Resolve-Path -LiteralPath $ResourceSource).Path)
$ns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
$xns='http://schemas.microsoft.com/winfx/2006/xaml'
$ui='using:MemoAI.App'
$output=[xml]::new()
$root=$output.CreateElement('ResourceDictionary',$ns);[void]$output.AppendChild($root)
foreach($attribute in $source.DocumentElement.Attributes){[void]$root.Attributes.Append($output.ImportNode($attribute,$true))}
$root.SetAttribute('xmlns:ui',$ui)
[void]$root.AppendChild($output.CreateComment(' Copyright (c) Microsoft Corporation. All Rights Reserved. Adapted from Microsoft.WindowsAppSDK.WinUI 2.3.9; see Assets/Licenses/WindowsAppSDK.txt. Native parts, states and input behavior are retained; only painting is delegated to ContinuousSurface. Regenerate with scripts/update-native-surfaces.ps1. '))
foreach($pair in @(@('UniformSurfaceRadiusConverter','NativeUniformRadius'),@('UniformSurfaceThicknessConverter','NativeUniformThickness'),@('NegativeSurfaceThicknessConverter','NativeNegativeThickness'))){$converter=$output.CreateElement('ui',$pair[0],$ui);[void]$converter.SetAttribute('Key',$xns,$pair[1]);[void]$root.AppendChild($converter)}
$types=@('Button','ToggleButton','RepeatButton','HyperlinkButton','DropDownButton','TextBox','PasswordBox','ComboBox','ComboBoxItem','CheckBox','RadioButton','Slider','ScrollBar','ProgressBar','FlyoutPresenter','MenuFlyoutPresenter','MenuFlyoutItem','ToolTip','ContentDialog')
$keys=@('AccentButtonStyle','SubtleButtonStyle','MUX_NavigationViewItemPresenterStyleWhenOnLeftPane')+@($types|ForEach-Object {'Default'+$_+'Style'})
$styles=@($source.DocumentElement.ChildNodes | Where-Object {$_.LocalName -eq 'Style' -and $_.GetAttribute('Key',$xns) -in $keys})
$styles+=@($source.DocumentElement.ChildNodes | Where-Object {$_.LocalName -eq 'Style' -and !$_.GetAttribute('Key',$xns) -and $_.GetAttribute('TargetType') -in @('controls:NavigationViewItem','controls:NumberBox','controls:Expander')})
$lookup=@{}
foreach($node in $source.DocumentElement.ChildNodes){
    if($node -isnot [Xml.XmlElement]){continue}
    $key=$node.GetAttribute('Key',$xns);if(!$key){$key=$node.GetAttribute('Name',$xns)}
    if($key){$lookup[$key]=$node}
}
$themeMaps=@{}
foreach($theme in $source.DocumentElement.SelectNodes('./*[local-name()="ResourceDictionary.ThemeDictionaries"]/*')){
    $themeMap=[ordered]@{};foreach($node in $theme.ChildNodes){if($node -is [Xml.XmlElement]){$key=$node.GetAttribute('Key',$xns);if(!$key){$key=$node.GetAttribute('Name',$xns)};if($key){$themeMap[$key]=$node}}}
    $themeMaps[$theme.GetAttribute('Key',$xns)]=$themeMap
}
$ordered=[Collections.Generic.List[Xml.XmlNode]]::new();$seen=[Collections.Generic.HashSet[string]]::new()
function Get-ResourceReferences($node){
    foreach($match in [regex]::Matches($node.OuterXml,'\{(?:StaticResource|ThemeResource) ([^}]+)\}|\bResourceKey="([^"]+)"')){
        if($match.Groups[1].Success){$match.Groups[1].Value}else{$match.Groups[2].Value}
    }
}
function Add-Dependency($key){
    if(!$seen.Add($key)){return}
    if($lookup.ContainsKey($key)){
        $node=$lookup[$key]
        foreach($reference in Get-ResourceReferences $node){Add-Dependency $reference}
        $ordered.Add($node)
    }
    foreach($themeKey in @('Default','Light','HighContrast')){$themeMap=$themeMaps[$themeKey];if($themeMap -and $themeMap.Contains($key)){foreach($reference in Get-ResourceReferences $themeMap[$key]){Add-Dependency $reference}}}
}
foreach($style in $styles){
    foreach($reference in Get-ResourceReferences $style){Add-Dependency $reference}
    $key=$style.GetAttribute('Key',$xns)
    if($key){Add-Dependency $key}else{$ordered.Add($style)}
}
$themeDictionaries=$output.CreateElement('ResourceDictionary.ThemeDictionaries',$ns)
foreach($themeKey in @('Default','Light','HighContrast')){
    if(!$themeMaps.ContainsKey($themeKey)){continue}
    $theme=$output.CreateElement('ResourceDictionary',$ns);[void]$theme.SetAttribute('Key',$xns,$themeKey)
    foreach($key in $themeMaps[$themeKey].Keys){if($seen.Contains($key)){[void]$theme.AppendChild($output.ImportNode($themeMaps[$themeKey][$key],$true))}}
    [void]$themeDictionaries.AppendChild($theme)
}
[void]$root.PrependChild($themeDictionaries)
$styles=$ordered.ToArray()
$serial=0
function Test-TemplateScope($node,$template){
    $cursor=$node.ParentNode
    while($cursor){if($cursor.LocalName -eq 'ControlTemplate'){return [object]::ReferenceEquals($cursor,$template)};$cursor=$cursor.ParentNode}
    return $false
}
foreach($style in $styles){
    $clone=$output.ImportNode($style,$true)
    foreach($template in @($clone.SelectNodes('.//*') | Where-Object {$_.LocalName -eq 'ControlTemplate'})){
        $map=@{}
        # Keep the original named parts and their CLR types: WinUI's code uses them.
        $nodes=@($template.SelectNodes('.//*') | Where-Object {(Test-TemplateScope $_ $template) -and (($_.LocalName -in @('Grid','Border','ContentPresenter') -and $_.HasAttribute('CornerRadius') -and ($_.HasAttribute('Background') -or $_.HasAttribute('BorderBrush'))) -or ($_.LocalName -eq 'Rectangle' -and $_.HasAttribute('RadiusX')))})
        foreach($node in $nodes){
            $serial++;$name=$node.GetAttribute('Name',$xns)
            if(!$name){$name='NativePaintPart'+$serial;[void]$node.SetAttribute('Name',$xns,$name)}
            # NavigationView animates this UIElement's composition visual directly.
            # Keep the name on the painted surface itself, not on an invisible sibling.
            # NavigationViewHelper.h explicitly resolves UIElement, not Rectangle.
            if($node.LocalName -eq 'Rectangle' -and $name -eq 'SelectionIndicator'){
                $indicator=$output.CreateElement('ui','ContinuousSurface',$ui)
                foreach($attribute in $node.Attributes){
                    if($attribute.LocalName -notin @('Fill','Stroke','StrokeThickness','RadiusX','RadiusY')){[void]$indicator.Attributes.Append($output.ImportNode($attribute,$true))}
                }
                $indicator.SetAttribute('IsHitTestVisible','False')
                $indicator.SetAttribute('Background',$node.GetAttribute('Fill'))
                $radius=$node.GetAttribute('RadiusX')
                if($radius.StartsWith('{Binding')){$radius='{TemplateBinding CornerRadius}'}
                $indicator.SetAttribute('CornerRadius',$radius)
                [void]$node.ParentNode.ReplaceChild($indicator,$node)
                continue
            }
            $surface=$output.CreateElement('ui','ContinuousSurface',$ui);[void]$surface.SetAttribute('Name',$xns,'G2_'+$name)
            $surface.SetAttribute('IsHitTestVisible','False')
            foreach($property in @('Background','BorderBrush','BorderThickness','CornerRadius')){
                if($node.HasAttribute($property)){$surface.SetAttribute($property,$node.GetAttribute($property))}
            }
            if($node.LocalName -eq 'Grid'){
                # Preserve Grid roots and immediate children used by native layout code.
                # The paint child spans the grid and extends over its original border inset.
                $surface.SetAttribute('Grid.RowSpan',[string][Math]::Max(1,@($node.ChildNodes | Where-Object {$_.LocalName -eq 'Grid.RowDefinitions'} | ForEach-Object {$_.ChildNodes | Where-Object {$_.LocalName -eq 'RowDefinition'}}).Count))
                $surface.SetAttribute('Grid.ColumnSpan',[string][Math]::Max(1,@($node.ChildNodes | Where-Object {$_.LocalName -eq 'Grid.ColumnDefinitions'} | ForEach-Object {$_.ChildNodes | Where-Object {$_.LocalName -eq 'ColumnDefinition'}}).Count))
                $surface.SetAttribute('Margin','{Binding BorderThickness, ElementName='+$name+', Converter={StaticResource NativeNegativeThickness}}')
                $node.SetAttribute('Background','Transparent');$node.RemoveAttribute('BorderBrush');$node.SetAttribute('CornerRadius','0')
                $firstChild=$node.ChildNodes | Where-Object {$_ -is [Xml.XmlElement] -and !$_.LocalName.Contains('.')} | Select-Object -First 1
                if($firstChild){[void]$node.InsertBefore($surface,$firstChild)}else{[void]$node.AppendChild($surface)}
                $map[$name]=@{Surface='G2_'+$name;Layout=$name;LayoutProperties=@()}
                continue
            }
            if($node.LocalName -eq 'Border'){
                $node.SetAttribute('Background','Transparent');$node.RemoveAttribute('BorderBrush');$node.SetAttribute('CornerRadius','0')
                $surface.SetAttribute('Margin','{Binding BorderThickness, ElementName='+$name+', Converter={StaticResource NativeNegativeThickness}}')
                $content=$node.ChildNodes | Where-Object {$_ -is [Xml.XmlElement] -and !$_.LocalName.Contains('.')} | Select-Object -First 1
                if($content){
                    $layer=$output.CreateElement('Grid',$ns);[void]$node.ReplaceChild($layer,$content);[void]$layer.AppendChild($surface);[void]$layer.AppendChild($content)
                }else{[void]$node.AppendChild($surface)}
                $map[$name]=@{Surface='G2_'+$name;Layout=$name;LayoutProperties=@()}
                continue
            }
            if($node.LocalName -eq 'Rectangle'){
                if($node.HasAttribute('Fill')){$surface.SetAttribute('Background',$node.GetAttribute('Fill'));$node.RemoveAttribute('Fill')}
                if($node.HasAttribute('Stroke')){$surface.SetAttribute('BorderBrush',$node.GetAttribute('Stroke'));$node.RemoveAttribute('Stroke')}
                $surface.SetAttribute('CornerRadius','{Binding RadiusX, ElementName='+$name+', Converter={StaticResource NativeUniformRadius}}')
                $surface.SetAttribute('BorderThickness','{Binding StrokeThickness, ElementName='+$name+', Converter={StaticResource NativeUniformThickness}}')
            }
            $wrapper=$output.CreateElement('Grid',$ns);[void]$wrapper.SetAttribute('Name',$xns,'G2Layout_'+$name)
            $wrapper.SetAttribute('Background','Transparent')
            $layout=@('Width','Height','MinWidth','MinHeight','MaxWidth','MaxHeight','Margin','HorizontalAlignment','VerticalAlignment','Visibility','Opacity','RenderTransform','RenderTransformOrigin','Grid.Row','Grid.Column','Grid.RowSpan','Grid.ColumnSpan','Canvas.Left','Canvas.Top')
            foreach($property in $layout){if($node.HasAttribute($property)){$wrapper.SetAttribute($property,$node.GetAttribute($property));$node.RemoveAttribute($property)}}
            $node.RemoveAttribute('Background');$node.RemoveAttribute('BorderBrush');if($node.LocalName -ne 'Rectangle'){$node.SetAttribute('CornerRadius','0')}
            $parent=$node.ParentNode
            [void]$parent.ReplaceChild($wrapper,$node)
            if($parent.LocalName -eq 'ControlTemplate'){
                foreach($groups in @($node.ChildNodes | Where-Object {$_.LocalName -eq 'VisualStateManager.VisualStateGroups'})){[void]$node.RemoveChild($groups);[void]$wrapper.AppendChild($groups)}
            }
            [void]$wrapper.AppendChild($surface);[void]$wrapper.AppendChild($node)
            $map[$name]=@{Surface='G2_'+$name;Layout='G2Layout_'+$name;LayoutProperties=$layout}
        }
        foreach($element in $template.SelectNodes('.//*')){
            if(!(Test-TemplateScope $element $template)){continue}
            if($element.HasAttribute('Target')){
                $target=$element.GetAttribute('Target')
                foreach($name in $map.Keys){
                    foreach($property in @('Background','BorderBrush','BorderThickness','CornerRadius')){if($target -eq "$name.$property"){$element.SetAttribute('Target',$map[$name].Surface+".$property")}}
                    if($target -eq "$name.Fill"){$element.SetAttribute('Target',$map[$name].Surface+'.Background')}
                    if($target -eq "$name.Stroke"){$element.SetAttribute('Target',$map[$name].Surface+'.BorderBrush')}
                    foreach($property in $map[$name].LayoutProperties){if($target -eq "$name.$property" -or $target -eq "$name.($property)"){$element.SetAttribute('Target',$map[$name].Layout+$target.Substring($name.Length))}}
                }
            }
            if($element.HasAttribute('Storyboard.TargetName')){
                $name=$element.GetAttribute('Storyboard.TargetName');$property=$element.GetAttribute('Storyboard.TargetProperty')
                if($map.ContainsKey($name)){
                    if($property -in @('Background','BorderBrush','BorderThickness','CornerRadius')){$element.SetAttribute('Storyboard.TargetName',$map[$name].Surface)}
                    elseif($property -in @('Fill','Stroke')){$element.SetAttribute('Storyboard.TargetName',$map[$name].Surface);$element.SetAttribute('Storyboard.TargetProperty',$(if($property -eq 'Fill'){'Background'}else{'BorderBrush'}))}
                    elseif(!$property -or $property -in $map[$name].LayoutProperties){$element.SetAttribute('Storyboard.TargetName',$map[$name].Layout)}
                }
            }
        }
    }
    [void]$root.AppendChild($clone)
}
foreach($type in $types){
    $style=$output.CreateElement('Style',$ns);$style.SetAttribute('TargetType',$type);$style.SetAttribute('BasedOn','{StaticResource Default'+$type+'Style}');[void]$root.AppendChild($style)
}
$settings=[Xml.XmlWriterSettings]::new();$settings.Indent=$true;$settings.Encoding=[Text.UTF8Encoding]::new($false)
$destination=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/MemoAI.App/NativeContinuousStyles.xaml'
$writer=[Xml.XmlWriter]::Create($destination,$settings)
try{$output.Save($writer)}finally{$writer.Dispose()}
$licenseDirectory=Join-Path (Split-Path $PSScriptRoot -Parent) 'src/MemoAI.App/Assets/Licenses'
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
$packageRoot=Split-Path (Split-Path (Split-Path (Split-Path (Split-Path $ResourceSource -Parent) -Parent) -Parent) -Parent) -Parent
Copy-Item -LiteralPath (Join-Path $packageRoot 'license.txt') -Destination (Join-Path $licenseDirectory 'WindowsAppSDK.txt') -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'NOTICE.txt') -Destination (Join-Path $licenseDirectory 'WindowsAppSDK-NOTICE.txt') -Force
Write-Host "Generated $serial initial-frame surfaces in $($styles.Count) native resources."
