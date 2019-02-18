$maxIssueId = 1656;
# $maxIssueId = 1;
# md -Force ./issues 


for ($i=1640; $i -le $maxIssueId; $i++){
    iwr "https://wow.curseforge.com/projects/tellmewhen/issues/$i" -OutFile ./html/$i.html
    # $html = New-Object -ComObject "HTMLFile"
    # $html.IHtmlDocument2_write($(get-content ./issues/$i.html -raw))
    # $el = $html.all | Where className -Match 'project-issue-details'
    write-host $i
}