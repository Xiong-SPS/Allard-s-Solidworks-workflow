if ([SpsWorkflow.PropertyLengthFormat]::Format(1.9375) -ne '1.938') { throw 'Three-decimal rounding failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::Format(2) -ne '2' -or [SpsWorkflow.PropertyLengthFormat]::Format(1.25) -ne '1.25') { throw 'Maximum rather than forced precision failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::Literal('1.9375 in') -ne '1.938 in') { throw 'Standalone length formatting failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::Description('PIPE, 3 INCH, 1.9375 IN STOCK LENGTH, 304/304L') -ne 'PIPE, 3 INCH, 1.938 IN STOCK LENGTH, 304/304L') { throw 'Description length formatting failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::Description('STOCK LENGTH 10.452123 IN; ERP 06030612322') -ne 'STOCK LENGTH 10.452 IN; ERP 06030612322') { throw 'Labeled length formatting failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::Description('Bore 3.12345; tolerance .0001; part ABC12345') -ne 'Bore 3.12345; tolerance .0001; part ABC12345') { throw 'Unrelated values were changed.' }
Write-Output 'PASS length formatting: three-decimal maximum, rounding, units, descriptions and unrelated identifiers/dimensions preserved.'
if ([SpsWorkflow.PropertyLengthFormat]::CopedDescription('PIPE, 2-1/2" SC. 10, "RD1@Annotations""','8.22','in') -ne 'PIPE, 2-1/2" SC. 10, 8.22"') { throw 'Screenshot-style finished-length substitution failed.' }
if ([SpsWorkflow.PropertyLengthFormat]::CopedDescription('PIPE 3 INCH SCH10, 6 IN STOCK LENGTH','3.151','in') -ne 'PIPE 3 INCH SCH10, MAX MODELED LENGTH 3.151 in') { throw 'Stock length was retained in the finished-length description.' }
Write-Output 'PASS coped descriptions: screenshot-style length and explicit stock-length replacement.'
