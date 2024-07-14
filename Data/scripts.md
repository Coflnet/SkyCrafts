```js
// extracted from https://wiki.hypixel.net/The_Forge#Collection into forge_requirements.json
let rows = document.querySelectorAll('tr:has(td:nth-child(2) a)');

let result = {};
// Extract the data from the last <td> in the found <tr> element
rows.forEach(row => {
    let innerLinks = row.querySelectorAll('td a');
    if(innerLinks.length <= 1)
        return;
    var link = innerLinks[1].innerText.replace("https://wiki.hypixel.net/","");
    let links = row.querySelectorAll('td:last-child>a');
    let data = Array.from(links).map(link => link.innerText.trim()).filter(l=>l);
    if(data.length)
    result[link]=data
});
console.log(JSON.stringify(result))
```