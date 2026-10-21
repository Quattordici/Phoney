namespace Phoney.Data;

/// <summary>
/// The ISO 3166-1 alpha-2 code of each country in faker.js' English country list (<c>location.country</c> of <c>en</c>), so an object's country and
/// country code can name the same country in a locale that has no country of its own. faker.js has no such mapping; the table pairs its 249 names
/// with its 249 codes one to one and was checked against pycountry (ISO 3166-1).
/// </summary>
/// <remarks>
/// The table is one string (<c>code=name|</c>), parsed the first time an object needs both a country and a code, so it costs nothing on the
/// first call of a process (see <c>CharMappings</c>).
/// </remarks>
internal static class CountryNames
{
    private const string Pairs =
        "AD=Andorra|AE=United Arab Emirates|AF=Afghanistan|AG=Antigua and Barbuda|AI=Anguilla|AL=Albania|AM=Armenia|AO=Angola|AQ=Antarctica|AR=Argentina|" +
        "AS=American Samoa|AT=Austria|AU=Australia|AW=Aruba|AX=Åland Islands|AZ=Azerbaijan|BA=Bosnia and Herzegovina|BB=Barbados|BD=Bangladesh|BE=Belgium|" +
        "BF=Burkina Faso|BG=Bulgaria|BH=Bahrain|BI=Burundi|BJ=Benin|BL=Saint Barthélemy|BM=Bermuda|BN=Brunei Darussalam|BO=Bolivia|" +
        "BQ=Bonaire, Sint Eustatius and Saba|BR=Brazil|BS=Bahamas|BT=Bhutan|BV=Bouvet Island|BW=Botswana|BY=Belarus|BZ=Belize|CA=Canada|" +
        "CC=Cocos (Keeling) Islands|CD=Democratic Republic of the Congo|CF=Central African Republic|CG=Congo|CH=Switzerland|CI=Côte d'Ivoire|CK=Cook Islands|" +
        "CL=Chile|CM=Cameroon|CN=China|CO=Colombia|CR=Costa Rica|CU=Cuba|CV=Cape Verde|CW=Curaçao|CX=Christmas Island|CY=Cyprus|CZ=Czechia|DE=Germany|" +
        "DJ=Djibouti|DK=Denmark|DM=Dominica|DO=Dominican Republic|DZ=Algeria|EC=Ecuador|EE=Estonia|EG=Egypt|EH=Western Sahara|ER=Eritrea|ES=Spain|ET=Ethiopia|" +
        "FI=Finland|FJ=Fiji|FK=Falkland Islands (Malvinas)|FM=Micronesia|FO=Faroe Islands|FR=France|GA=Gabon|GB=United Kingdom|GD=Grenada|GE=Georgia|" +
        "GF=French Guiana|GG=Guernsey|GH=Ghana|GI=Gibraltar|GL=Greenland|GM=Gambia|GN=Guinea|GP=Guadeloupe|GQ=Equatorial Guinea|GR=Greece|" +
        "GS=South Georgia and the South Sandwich Islands|GT=Guatemala|GU=Guam|GW=Guinea-Bissau|GY=Guyana|HK=Hong Kong|HM=Heard Island and McDonald Islands|" +
        "HN=Honduras|HR=Croatia|HT=Haiti|HU=Hungary|ID=Indonesia|IE=Ireland|IL=Israel|IM=Isle of Man|IN=India|" +
        "IO=British Indian Ocean Territory (Chagos Archipelago)|IQ=Iraq|IR=Iran|IS=Iceland|IT=Italy|JE=Jersey|JM=Jamaica|JO=Jordan|JP=Japan|KE=Kenya|" +
        "KG=Kyrgyz Republic|KH=Cambodia|KI=Kiribati|KM=Comoros|KN=Saint Kitts and Nevis|KP=Democratic People's Republic of Korea|KR=Republic of Korea|" +
        "KW=Kuwait|KY=Cayman Islands|KZ=Kazakhstan|LA=Lao People's Democratic Republic|LB=Lebanon|LC=Saint Lucia|LI=Liechtenstein|LK=Sri Lanka|LR=Liberia|" +
        "LS=Lesotho|LT=Lithuania|LU=Luxembourg|LV=Latvia|LY=Libyan Arab Jamahiriya|MA=Morocco|MC=Monaco|MD=Moldova|ME=Montenegro|MF=Saint Martin|MG=Madagascar|" +
        "MH=Marshall Islands|MK=North Macedonia|ML=Mali|MM=Myanmar|MN=Mongolia|MO=Macao|MP=Northern Mariana Islands|MQ=Martinique|MR=Mauritania|MS=Montserrat|" +
        "MT=Malta|MU=Mauritius|MV=Maldives|MW=Malawi|MX=Mexico|MY=Malaysia|MZ=Mozambique|NA=Namibia|NC=New Caledonia|NE=Niger|NF=Norfolk Island|NG=Nigeria|" +
        "NI=Nicaragua|NL=Netherlands|NO=Norway|NP=Nepal|NR=Nauru|NU=Niue|NZ=New Zealand|OM=Oman|PA=Panama|PE=Peru|PF=French Polynesia|PG=Papua New Guinea|" +
        "PH=Philippines|PK=Pakistan|PL=Poland|PM=Saint Pierre and Miquelon|PN=Pitcairn Islands|PR=Puerto Rico|PS=Palestine|PT=Portugal|PW=Palau|PY=Paraguay|" +
        "QA=Qatar|RE=Réunion|RO=Romania|RS=Serbia|RU=Russian Federation|RW=Rwanda|SA=Saudi Arabia|SB=Solomon Islands|SC=Seychelles|SD=Sudan|SE=Sweden|" +
        "SG=Singapore|SH=Saint Helena|SI=Slovenia|SJ=Svalbard & Jan Mayen Islands|SK=Slovakia|SL=Sierra Leone|SM=San Marino|SN=Senegal|SO=Somalia|SR=Suriname|" +
        "SS=South Sudan|ST=Sao Tome and Principe|SV=El Salvador|SX=Sint Maarten|SY=Syrian Arab Republic|SZ=Eswatini|TC=Turks and Caicos Islands|TD=Chad|" +
        "TF=French Southern Territories|TG=Togo|TH=Thailand|TJ=Tajikistan|TK=Tokelau|TL=Timor-Leste|TM=Turkmenistan|TN=Tunisia|TO=Tonga|TR=Turkey|" +
        "TT=Trinidad and Tobago|TV=Tuvalu|TW=Taiwan|TZ=Tanzania|UA=Ukraine|UG=Uganda|UM=United States Minor Outlying Islands|US=United States of America|" +
        "UY=Uruguay|UZ=Uzbekistan|VA=Holy See (Vatican City State)|VC=Saint Vincent and the Grenadines|VE=Venezuela|VG=Virgin Islands, British|" +
        "VI=Virgin Islands, U.S.|VN=Vietnam|VU=Vanuatu|WF=Wallis and Futuna|WS=Samoa|YE=Yemen|YT=Mayotte|ZA=South Africa|ZM=Zambia|ZW=Zimbabwe|";

    /// <summary>The English name faker.js uses for <paramref name="alpha2"/>, or <see langword="null"/>.</summary>
    public static string? NameOf(string alpha2) => Table.Names.GetValueOrDefault(alpha2);

    /// <summary>The alpha-2 code of the English country name <paramref name="name"/>, or <see langword="null"/>.</summary>
    public static string? CodeOf(string name) => Table.Codes.GetValueOrDefault(name);

    private static class Table
    {
        public static readonly Dictionary<string, string> Names = Parse(byName: false);
        public static readonly Dictionary<string, string> Codes = Parse(byName: true);

        private static Dictionary<string, string> Parse(bool byName)
        {
            var map = new Dictionary<string, string>(260, StringComparer.Ordinal);
            foreach (var pair in Pairs.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                var (code, name) = (pair[..separator], pair[(separator + 1)..]);
                if (byName)
                    map.Add(name, code);
                else
                    map.Add(code, name);
            }

            return map;
        }
    }
}
