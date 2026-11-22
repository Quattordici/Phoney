namespace Phoney.Data;

internal static partial class CountryNames
{
    /// <summary>
    /// The German names: every ISO 3166-1 code with the name faker.js' <c>de</c> list (<c>location.country</c>) gives it, else (no name, or more
    /// than one) the CLDR name, followed by faker.js' other names and the CLDR name as more names of the same code. The first name of a code is the
    /// one a given code gets, so a given code and a drawn country use the same words. Built from faker.js' list matched to codes with CLDR (Babel) and
    /// cross-checked with the iso-codes translations pycountry ships; the former countries in faker.js' list without a code are left out.
    /// </summary>
    private const string GermanPairs =
        "AD=Andorra|AE=Vereinigte Arabische Emirate|AF=Afghanistan|AG=Antigua und Barbuda|AI=Anguilla|AL=Albanien|AM=Armenien|AO=Angola|AQ=Antarktis|" +
        "AR=Argentinien|AS=Amerikanisch-Samoa|AT=Österreich|AU=Australien|AW=Aruba|AX=Ålandinseln|AZ=Aserbaidschan|BA=Bosnien und Herzegowina|" +
        "BB=Barbados|BD=Bangladesch|BE=Belgien|BF=Burkina Faso|BG=Bulgarien|BH=Bahrain|BI=Burundi|BJ=Benin|BL=St. Barthélemy|BM=die Bermudas|" +
        "BM=Bermuda|BN=Brunei Darussalam|BO=Bolivien|BQ=Karibische Niederlande|BR=Brasilien|BS=Bahamas|BT=Bhutan|BV=Bouvetinsel|BW=Botsuana|" +
        "BY=Belarus|BZ=Belize|CA=Kanada|CC=Kokosinseln|CD=Demokratische Republik Kongo|CD=Kongo-Kinshasa|CF=Zentralafrikanische Republik|CG=Kongo|" +
        "CG=Kongo-Brazzaville|CH=Schweiz|CI=Côte d’Ivoire|CK=Cookinseln|CL=Chile|CM=Kamerun|CN=China|CO=Kolumbien|CR=Costa Rica|CU=Kuba|CV=Kap Verde|" +
        "CV=Cabo Verde|CW=Curaçao|CX=Weihnachtsinsel|CY=Zypern|CZ=Tschechische Republik|CZ=Tschechien|DE=Deutschland|DJ=Dschibuti|DK=Dänemark|" +
        "DM=Dominica|DO=Dominikanische Republik|DZ=Algerien|EC=Ecuador|EE=Estland|EG=Ägypten|EH=Westsahara|ER=Eritrea|ES=Spanien|ET=Äthiopien|" +
        "FI=Finnland|FJ=Fidschi|FK=Falklandinseln|FM=Mikronesien|FO=Färöer|FR=Frankreich|GA=Gabun|GB=Vereinigtes Königreich|GD=Grenada|GE=Georgien|" +
        "GF=Französisch-Guayana|GG=Guernsey|GH=Ghana|GI=Gibraltar|GL=Grönland|GM=Gambia|GN=Guinea|GP=Guadeloupe|GQ=Äquatorialguinea|GR=Griechenland|" +
        "GS=Südgeorgien und Südliche Sandwichinseln|GS=Südgeorgien und die Südlichen Sandwichinseln|GT=Guatemala|GU=Guam|GW=Guinea-Bissau|GY=Guyana|" +
        "HK=Hongkong|HK=Sonderverwaltungsregion Hongkong|HM=Heard und McDonaldinseln|HN=Honduras|HR=Kroatien|HT=Haiti|HU=Ungarn|ID=Indonesien|" +
        "IE=Irland|IL=Israel|IM=Isle of Man|IN=Indien|IO=Britisches Territorium im Indischen Ozean|IQ=Irak|IR=Iran|IS=Island|IT=Italien|JE=Jersey|" +
        "JM=Jamaika|JO=Jordanien|JP=Japan|KE=Kenia|KG=Kirgisistan|KH=Kambodscha|KI=Kiribati|KM=Komoren|KN=St. Kitts und Nevis|" +
        "KP=Demokratische Volksrepublik Korea|KP=Nordkorea|KR=Republik Korea|KR=Südkorea|KW=Kuwait|KY=Kaimaninseln|KZ=Kasachstan|LA=Laos|LB=Libanon|" +
        "LC=St. Lucia|LI=Liechtenstein|LK=Sri Lanka|LR=Liberia|LS=Lesotho|LT=Litauen|LU=Luxemburg|LV=Lettland|LY=Libyen|MA=Marokko|MC=Monaco|" +
        "MD=Republik Moldau|ME=Montenegro|MF=St. Martin|MG=Madagaskar|MH=Marshallinseln|MK=ehemalige jugoslawische Republik Mazedonien|" +
        "MK=Nordmazedonien|ML=Mali|MM=Myanmar|MN=Mongolei|MO=Macau|MO=Sonderverwaltungsregion Macau|MP=Nördliche Marianen|MQ=Martinique|" +
        "MR=Mauretanien|MS=Montserrat|MT=Malta|MU=Mauritius|MV=Malediven|MW=Malawi|MX=Mexiko|MY=Malaysia|MZ=Mosambik|NA=Namibia|NC=Neukaledonien|" +
        "NE=Niger|NF=Norfolkinsel|NG=Nigeria|NI=Nicaragua|NL=Niederlande|NO=Norwegen|NP=Nepal|NR=Nauru|NU=Niue|NZ=Neuseeland|OM=Oman|PA=Panama|" +
        "PE=Peru|PF=Französisch-Polynesien|PG=Papua-Neuguinea|PH=Philippinen|PK=Pakistan|PL=Polen|PM=St. Pierre und Miquelon|PN=Pitcairninseln|" +
        "PR=Puerto Rico|PS=Palästinensische Autonomiegebiete|PT=Portugal|PW=Palau|PY=Paraguay|QA=Katar|RE=Réunion|RO=Rumänien|RS=Serbien|" +
        "RU=Russische Föderation|RU=Russland|RW=Ruanda|SA=Saudi-Arabien|SB=Salomonen|SC=Seychellen|SD=Sudan|SE=Schweden|SG=Singapur|SH=St. Helena|" +
        "SI=Slowenien|SJ=Svalbard und Jan Mayen|SJ=Spitzbergen und Jan Mayen|SK=Slowakei|SL=Sierra Leone|SM=San Marino|SN=Senegal|SO=Somalien|" +
        "SO=Somalia|SR=Surinam|SR=Suriname|SS=Südsudan|ST=São Tomé und Príncipe|SV=El Salvador|SX=Sint Maarten|SY=Syrien|SZ=Swasiland|SZ=Eswatini|" +
        "TC=Turks- und Caicosinseln|TD=Tschad|TF=Französische Gebiete im südlichen Indischen Ozean|TF=Französische Süd- und Antarktisgebiete|TG=Togo|" +
        "TH=Thailand|TJ=Tadschikistan|TK=Tokelau|TL=Osttimor|TL=Timor-Leste|TM=Turkmenistan|TN=Tunesien|TO=Tonga|TR=Türkei|TT=Trinidad und Tobago|" +
        "TV=Tuvalu|TW=Taiwan|TZ=Tansania|UA=Ukraine|UG=Uganda|UM=Kleinere amerikanische Überseeinseln|UM=Amerikanische Überseeinseln|" +
        "US=Vereinigte Staaten|UY=Uruguay|UZ=Usbekistan|VA=Vatikanstadt|VC=St. Vincent und die Grenadinen|VE=Venezuela|VG=Britische Jungferninseln|" +
        "VI=Amerikanische Jungferninseln|VN=Vietnam|VU=Vanuatu|WF=Wallis und Futuna|WS=Samoa|YE=Jemen|YT=Mayotte|ZA=Südafrika|ZM=Sambia|ZW=Simbabwe|";
}
