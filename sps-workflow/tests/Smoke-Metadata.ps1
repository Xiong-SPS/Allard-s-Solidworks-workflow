$known = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$inherited = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$inherited['Description'] = 'PIPE, 3" SC.10, "RD1@Annotations" "SW-Material@OLD.SLDPRT"'
$inherited['Part Description'] = 'PIPE, 3" SC.10, "RD1@Annotations"'
$inherited['Material ID'] = 'verified pipe stock'
$inherited['DrawnBy'] = 'OLD TEMPLATE DRAFTER'
$inherited['Drawn Date'] = 'OLD TEMPLATE DATE'
$values = [SpsWorkflow.MetadataPolicy]::Resolve($known,$inherited,'NEW-01.SLDPRT',$true,$false,$null)
if ($values['Description'] -notlike '*SW-Material@NEW-01.SLDPRT*' -or $values['Description'] -like '*RD1@*' -or $values['Description'] -notlike '*D1@Boss-Extrude1*') { throw 'Description links were not adapted.' }
if ($values['MakeOrBuy'] -ne 'M' -or $values['Material ID'] -ne 'verified pipe stock') { throw 'Pipe defaults/stock identity failed.' }
if ($values['DrawnBy'] -ne '' -or $values['Drawn Date'] -ne '') { throw 'Template drafter/date leaked into job metadata.' }
$known['MakeOrBuy'] = 'B'
$values = [SpsWorkflow.MetadataPolicy]::Resolve($known,$inherited,'NEW-01.SLDPRT',$false,$true,$null)
if ($values['MakeOrBuy'] -ne 'B' -or $values['Material ID'] -ne '' -or $values['Description'] -ne '') { throw 'Modified fitting metadata policy failed.' }
Write-Output 'PASS metadata policy: current links, known values, M default, pipe stock preservation, fitting stock flag and no borrowed drafter/date.'
