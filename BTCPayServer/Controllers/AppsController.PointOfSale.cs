using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers
{
    public partial class AppsController
    {
        public class PointOfSaleSettings
        {
            private const string posDefaultImage =
                "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCADOASwDASIAAhEBAxEB/8QAHQAAAgMBAQEBAQAAAAAAAAAABAUDBgcCCAEACf/EAEwQAAEDAwMCBAMEBwUFBgQHAAECAwQABREGEiExQQcTIlEUYXEygZGhCBUjQrHB8DNSYnLRFoKS4fEJJENEU9IXJTTCY2SDk6Kjsv/EABwBAAEFAQEBAAAAAAAAAAAAAAMBAgQFBgAHCP/EADcRAAEDAwIDBgUDBAEFAAAAAAEAAgMEESESMQVBURNhcYGR8AYiobHBMtHhFBUjQvEWJENScv/aAAwDAQACEQMRAD8A99MHBx70UUj2oZrA59qKQrI+dSlDCicRlPFCvR88g/nR+056VypvdkU4FNISdxkkZyfxoVbRPc03cjkqoZ2MQelEBTClTjSs9TX4IPvRq2eea4U18q5JZQBPTNdBPNdoQT2NShv5YrktlElOTzxXSWyOtShvB4rviuSKMHAxXaSa+lIV2r9jHauTl+IrlTQcTgjmpU9K6CecYz9K5M3S8M4UR3r6WjU892PESFPPIZPstWD+FLnL/GA/ZpefP+Bs4/E4rg8HYruzIyQii0ajU1yeKDVfXlf2VucPzWtKf9ajXdbgrpBaT/mcJ/lRAUlgjfLA96+7aWmZdFf+XjJ+pVXz4q6DqzFP+8qlSWCYlrJ4qNbGDQJuNyT/AOTYUPk6R/Kv366lp/tLYoj3beB/iBS3PRdYI3yeKiU1jtUKL+0Dh2LKa+Zb3D8iaIbuUKSQESWwr+6s7D+BxXahzXFp5KFSMVyU7u1GOskAEg4PQ9jQ5GD0pwzsmKBwFfUkn50MtoijlCoyARSgLkCQRXB70U43QyxT7JihVxUKxkGplVC4rFOXEodxNCOpyKNVyKGc7g05IgnU1AcDip3eFVArrXLrLc2BkUU0MKFBtcE0S2vOMVUqxRm3ckYqJxGCDUqF5GK6UgFOKauQqxk1C6zkfKiFJJP0qUIyjBFP1WTSLpMtnnmolMYGaausAZofywM5p+oIdiEB5eK/Y7VK4ClRrpKNyc964my5cJb4ya+Kb4yKmT6QR1r59ntwaaClsh08GunCltBWohKEgkqUcAD3Jr48UtBS1HakDJJ7Ck11aF2lRIrij8IGW5TjfZ0ryUJPukAZI7kj2pXOsEgFzZfP9oVzV4tkX4hvp8U9lLR/yjqr68D50SW5chGJMteO7bOG0/l1+8mpXlojJytSUIAz7Cq9cteW6IstNrMh3ptbGaGbO/VlGHy7Jy1AjtZIbGfc9TXWGwDwkGqyzcb/AHs/90ipjNK6KcPNGN6Dvc0bpN02A/utppn9RGMX9P4Xdm85smS5TaOq0p++oV3iK39p9H41FH8M2fMAky5Lxzyd+B+VGL0DZIqf7Fx1Q7rWf9aQ1Tb6QCkEDrXugVaigjq+moF6hgkEeemmsfRVpdXhMUf6UcNFWVlnf8IlZ6c051U1ptbKRsTnZ5KtIvsP/wBdP40Q1d4qzw8j8abq0TZdu92GMHolPFfB4eWNYKloWznolKzmkNaxu4Sime7ZAiQw6MoWk/Q1w6y06MLQlQ+YzXUvQdqYBLU6Q0fYKzS5WmJTIzFu2fYOpOPxFK2ridzsudTvHJTJipjKzHW5GP8A+Esgfh0oxq4LQAmUw3LR/fb/AGTg/D0n8KUOMXyGnLkVMxsfvx1BX5daiTeWVEIcSphf91YxUgFj9j6IZDm7qxIYZnkiC6XHOvwzqdrv3Dor7jn5UIoYJHQjqKWKeCwOQe4Pt9KPiXtu6ym4ExYTOc9LEpRx5iuyHPmegV1989jBxbk5CFYHZfFnmoXEAiuirO4EEKSopUk9UkHBB+YNRqVRgUMhDuJoVzjINGLP4UK+M05NQyzzUDuFDBqVYIBqBawOtOSFCujHFDK4Jol47iSKEWTnpSpVuKFcZqVpWDihm1ZwKmQenvVUp5R7Ks9alS6AraTzQiHMHpUi+uRTUqL2jNSbcjFAh87etEx3CpJ5pLLl+W3uGKDcbwog0cSSnIqF1G9G7vXBccpe42FD6VFjAwKJUkhWccVGUkK+VEQtlEjke1djHSvu3KuK4XlKgDTbZXXS3UsF24aeukZhZQ+9FdbbUOqVFBAP4ms7a8VbXG8LTrOaspFkhiJd4rKCp1l5v0o9HXC+gz34PIrV3U5Qe/FeWfES2XmzXgav0HLaZvUhbjFytcgD4WencsELQeMq2qByMKIzweaY8Ei7UaO18pHp7W3iZ+kBd0OMRk6QsCTuLKsOyCjPG89Ekj93BPzFei9H+HUSyMth5xyVI7rWckn3rI/Crx/0VaJLlhv9qd8NdSKWVLt13y3EeXgD9i+rhI44CzgD981tKZGsL+lty2x4Fkt7g3JlPLEhxxPugIO0g9juqB8rt8n3yViXOabAWHvnzVrjsNwwrepLbYGBuwBUEjW+nrYktybi0tz/ANJn1r/Ac0jY8PY0x8uXmfMuzoPPmulLf0CE4GPrmrja7JbbOwEQobEdIH7jYFC+YnAsuIbu7PvvVUk66afUFW60XKUOxVHLYP3qxUcm9agksJcj6b2qPUSJCR/DNWqc4VLKRgioyvym0pA9Rrix1wdX2XBzLEafqVVYszVoC1G2W5njjMhR5/4amMjVqghPw1t4GVftVf8AtqwF8lW0gEDrmoi95qylHpz1NMdESbkn1RWOAbYAe/NImp2rCouOwLcsJ+yEvq/9tDqvmqPP2r0806D1W1JScfjirMtzag47HFRhZClc/aGaQQcw4+/JOMoA0lo9+aQMX51SliZZZcRKermzeD9MZoR3xF0c1IMdy5ttSumx/wBJH3HFWGc8oRSlJIHeqxKtcS6JU1KisyG1dUuoCgfxoH9K/XqdJjpb8hFNRGGBrI8+N/oUYi6JlILkKQw8nqFNqBNAzX1yWymWylwn94pGf+f31X5HhZaWXC7b25FodV+9AdU2n/g+z+VJbjb9eaW9dtnxdSRU8/CTk+S9j2Cx6SfqE1LjA1aSPNRHA6S7n0IVjfgP+Xm2pS5KJw3FW5sS+f7qFH7K/ZJOD0B7VSdSX4R7jbY2HmrhMkhhpnZ+0Ssdcg9MYo63eLVvkIlQtTWGdp9YaPnqnI8thA/vB/7HXoQrr0rCPFX9Km5+IdzY0r4cw0alv0dS2TqpTHlrbQoBJUFe4Ax5ytpwTgZwqpwmMXyk3v6qEY+0Fw2xHovSdr11a9U61v8ACgS2nn0zDH2IyN7zaEJdweivUR0zVgvMZdjbUqcpuPt6pW4nI+7NYh+j54Tu6a+GiLVKuk9qKFIDQJcW84875mwHAQkFn7RwB3Oa35/wFvctkvvRbcXVerylTFrWPkVFGM/l86lQyODbSGyjSMBPyi6qrOore+6Gky2g4eiFK2k/cetFLIxVOvmhxFlPNBosSGVELaJChkdeRwaHstymWWSiNIUp2AtW0bySpg9sE9U9sHpxUsSEZOR1H5CDoDtt+it7hyaEdFTuK9qhUQoc8GpAQEG4cHI6VCognk0RIG08YoMnJpyRbghPlgV0lfqoYSQWQe4roObkg+1VllYXR7a8p+eamQSqgW1kE/OpfOKOlMSojbtVUrTmxXyocOEpyDXXmc0qRGocycdjX0cHFCpWU81IHCo5H3U2yVcvDaSe1QqOEkUQ4rennrUJRxilCaQoQOM18WAQQetSEYoSbKZgtKekOpabSMlSzgCuOMoa/SJCIkVbzyw222NylK6AV498QvE63aE1xcLTeAE2masSGJuMpbUey/8AArnntuPY5Gq+KOtJFxnIjxnHGrcBtKkEEO5rzH+krAFys9mnJG7dHUwo/Ns4/hiqdnEWyTOij5C9+ql0jGzydmdivWWhdMaa1joO3Rpa036J5IBM1QkhQxwoFWSARzhJA5+VTWv9H5jSkgyNC6puujFFW8w4Docgun/FGdC2/vABr+bPht446x8GJqTYZ4cgbsqtssFbB55xzlBP+E89wa9peFP6eejdVMsRtSJc0rcjgKL37SOpXycAyPvApgrKeU6Hmx7/AN1dScOrKYa2Aub3Z9R/C3dp/wASLEB8ZFsWp2h/4zCnbc8fmcecgn6BI+lNYviX5QCblp+625Q4WsJafbH0KFlZ/wCAfSprLrK2aphCTabnFuLKujkV1LiT94Jo3zFkhSwlYPASsBWaNa2x/Kig6hlv4QP+29knv7kT0Np//MNLYx/+4lNFovkKQSWpkd728t5Kv4Gvkyw2+ajKobW89QE4/hVYmaUsjqlIVEKcdSlX+tOs5IA05Ktwd3oHIVnkmvhIDaiBgk1n7vhtZJGfL+KaV19DuKXu+FttzlF1ubX+R9Q/+6nWd0Tho5n6LS1PKHpxwBzmuUv4IzhI91VmCvC2Gvg3y77fb4hf/uqFzwcsL3pkTbjIz2W+efxpLu6BODY99R9P5V+umq7PC3JlXaBGI7PSUI/iaqVx8Y9F2pKy7qOM8pP7kEKkn/8AqCqWI8H9JQycQ3XSOpW5nNNouiNP29ALNojp9t6NxP4mhODj0Rm9m3qfQKnzv0jYDhKLJpvUF+dP2fLjJjNn/edUkj/hpDP1d4yawSU2bTdp0qwof/UXN9Uh5PzACQg/fWwRg1H9LMZplPs22E/wqv6w8RNN6SYW9ebxFhqSP7Nx0Kc+5AyaQNaz5pH2HkFznF/yxMue+5+iw25/oy3/AF7PS/4g67uOoGE8m3xFiNH/AMoxu49+B9aWajvWm/AlsWCxsQY0SVjy40Nna4tQ6uE7itZH95SiMDjg4oLxH/Snn3VD1v0lb/hWFgpVcrgPV/uND/7jg9xWKWS3SLxfXJk2Q7cbi9kuS5B3OLPJxn27Adh9Khf3WjY/sab5nH3kqzZwSvlZ29Z8jBm2x8hy81/RD9G/UsbSfhRapfwBkailsFL0t/GC2HFqQMjkj1qPbJJNWi4+IV/mOqX+sHGR/cZwgD8KqNitosmn7ZASMCNGba+8JANEOOfKtfHE1ouRlYeSQuJtsq9rGTdmlIvMOU4+7EPmPxHTuTIa/fTz0VjOMUZc7I05LcaQnLakpWkK/uLSFJz9yhXy9XBu1W6VLdKfKabKiFdDx0+/pRPh/MlaxU7cpLSUKdwA2gelCUpCUpHyAAFK5gZdw2I+qYHF1hzCESlTLaW1nKkDBPvUDqs0xvIDFykoAwErx+QpU4raTRY7hoTH21Gy4WvjBqBRBPY19dXnOKDU4c8HijJoWztLyrH3VO0vkg0AhwBWaKQcrGO9V6mXR7TpKenSpi75hJAx8qC3kKwOMipmFelznkDimEJboppRwR2r6FbVDNRIcAx25wa6kY29eaS2UiKbVkkHpUiTgihGl42k9xRAOTx2pEoUilAkntUKnMcHr71KoDYeeaDnTExWSsgq2jkAUNzgwaingFxsFMrKQD2rIvHSQ3ORb4PxoiLSStZz2xxkVpC9QRhAck7v2aU7h8/lXmzxBkr1ZepEl2YYTQUQMDcSKz/Ea6N1PpjN9SHNG6PDt0ukWsuQlxI9zEp0HekqTtHHaqFr2J+vPDqe2oZk22bvUjulCxtOfoUj8asTUWGj9nCly5ikAlTnmhKUY7kgdKyHxD8bkSLVaWrYyloOsSnLk0oev4hYLQJV+8EpQhY/zkHpWNgqRTzCQnrjryRqJ3Zyh52WF3yI2wp1ZUNiVEFWe+cY/GlC220LCVKSkqOEg9T9K5ubkh5hlC87dxUhvHPJ6/Prx99BoK27j56wJL2DwDwDjp8gKfM7tjrGF6JRcebFZjm4xnoP9j39w9VaItxuWnJbU6zXKXbH8DDsJ9TRz/ukVqOmP0w/FjSqEtjUQurSONtyYS6r/iGD+dYtAlqWy69NkNtoCtqEHAxjrjv3qWLcIlzecbYWXFIG4+ggY++o8c1RB+hxsPRbeNvC+JsY57W6n7A21HfkMi9r5Xqm0f8AaJarYCU3bS1vnf3lRZK2SfuIVVxt/wD2g2m5AT+sdJ3aMruWHWnh+ZSa8WKjAVymNk9DU1vGKlv+30Ca/wCFaB//AIyPAle/IX6d/hy+P2jN5iE9Q5Cz/BRpk3+mf4XyACbjOQfZUFdfz0EfH/Su22OaL/fKkdPRR/8Ao6hJ3d6/wv6GK/TC8NQkKFzkrSOwiKzSu4fpt6AjkmOzdZqsceXF2/8A+iK8Htx+aMajZUOKjv47VdR6fypcfwZw8bl3r/C9dXH9Oa2OEiDpGcodi/IbSPwGar98/TI1BdWEostkh21e3BkSnC+c/JGAB+Jrzk3H2pp1bomGhxVXPx2sth9vABW0PwjwphF4yfEn+FbL34pa51gFpumqrgWVdY8NYit49sNgEj6mkkW2tN5WEZX3Wo5UfqTzU8WLkDimSIu1usrVV885/wAryfNaan4dSUbf8EQb4AffdKjHwkkir74Mad/XGtrVHKdyS+lxQ/wo9Svy/jVUVH3KSjHJNb3+jDp4O3C4XhbeWmG/IbVj99R5x/ugfjWs+FYTPVNJ2GV5/wDF9SKeifbc49V6HWdw+dCOnaDnp71O4dvSqdqeVOvra7fbEHySry35GcAk/uJ9/nj6V7ncDcr53AJ2Vdv0t7xAvTdmt4LkBpwb1jo6sHr/AJQenuRnsM6imfM0XYnrXpq1put0jNFUh51zy48c7d20q/eWRztHyJxkZj0lo4aUgpixcIujqUl2Rt3fCoVwCB3cV0Qn35PANTX24tQoqbVBAQw3kLKVbsnOSN37xJ9SlH7SvkBQHv7V2huwRANA1HdIZEhcpannAA6s7lAHIyetCOH0nvUizk0O4rBPPNSggoV1WOR1oZSgT7VM6eSR1oVSuacu2WvJVzxR0ZzueopW0vJPPai2XMdetV6lBMDlSiO/UV2ws5BHQ+k0OHAAhQzmvqMpWtGfmK5Kj0nClJPepEr8xsj95NDBRcCT0NStrwseyuKRKpW3NzRT3HSpmHskZ69CKFbHlrINSDCVg9j0pCuTA+rgde1B3JKWITjr4LbeMZIoa6agesyGll/yGFHBWlGcH5mlt8uEbWtqcZhXXzX207i2FeoEfKqWormMcYQQHcrqwip3OAkIOnuVD1hdo1ms8+EXVuOLIUhIOCCfasKeiLvs1CZD6i2eExW1Y5+Zq6a+myLnAcgOvtxbvH6hZ+0gdVJP8qSabgR7dAmSJziYy4rPmOF3O5KehIxycZrAVJcHEPFuZ7l1ZG6SVobkW3WW671e7ou7uxYSPhUxW3mJDWPQ+h1pOCPZQJOD9Pc152ujzTDgffb3NbcJbxneokk/yrTPEGa7NdfdmS/1hIdW0w48ScqAyoEnoeEp55PvWcXRqK5cVvXFwswY7aUtsH+0cJG44x2yep7D8M5SSGofrO2beChtIbhV1u23G6KdlNMre6pLuAltHHcngYFL5rMS3pDZlKdkKHPwxGwD2yevSml91Y9cmkxUnyLU2coiNngfLP31WXVPTnlLAKieNo4AHsK0rBjKnMJO6DlPKdJDaTxwNx3ffRen0SYrjjgWhgrGFOLG44+QoyLp599XKktgjrtJJp/B0i4zx5Lc10jKUuLKeP8AL/1p7pG6S0KdHUOheHwus4bEb+SCjTitQzMWtOeSpIRu+QSMk/wpmxL+JfCfhlBGfUtTgB+fpwTn6/jTGFZpyVJMh5tpsf8Al2GwlJ+v+mKNdibFelASnsB2qonmhGBk/b0XpPw7w/jEznSySOjYbEki7ndw1XI9BvhKjHya7RH56UeI/wAqlRG+VVxlXrjIUI3H+VGsxto6URHiFRHFG/BEDGMVFfLyUpsYCgjw1OOhOKsEeJsQBiurfB9AUR6u9N2Im4jjiqiee+FJaywuuIkTpxR7kcJSOKLjRQhOelKtSX1i0RyVBTjp4Q02MqUfYCq1hdNIGNUWpe2Jhc7AQsx1xCkNxmy9MkLDMdsdVKPf6Dqa97eHejY2hfCay234ZBmraDz7x+0XFY6/dXj3wc0u85Oe1dJiOXF2ClAahJI9BUQVcfJOc/T517ThallaghRIUGG7HCkArccKVOEnqeQAlI/E17BwSIUkTXMN3Xvbr/AXgHxJWGumMZ/SlU59MebGiGPIfcfVjDaMpT/mVnirDB081Ell1lCA8lAUgOAlthH/AKiwOTk5wkcqPA7kP9NaIffSlaSFrGEqlOI/Zj/KkY3H6ce5r7qHVFr0hPgxWEJfZWsqclOK3ZX03EjqrtnokcDFbYPe3/PMfmIsByHPA6rBFrXDsoh8o59fHuVav13btrBhRN3nZUp1xwgubiMKUojjzCODjhIwkYxVTkwnWglRbUEq5GRWgXuBabsyidb0jzVKG9CT6Tz0pLq0SbzqcW6EwVFpCUIZTwfsg5qbS10Umlsffe/KyiT0sjLuf3WsqQ9uQTkdO1COr4yOlH3CO7FfW24kpcScEK6g0seOBkfeKvAbqsIsVC4rd9RQq1p3eoc1KtfcfhQ6+VU5KtUjvncn2IowOYSPl3pW0rCgTyaObWNpScjjFQiEdMGlZSR99SpcOUrP0zQjThCck/dRbakra+ppifuiWHAo4NTlC0rHGATxS9pzGeOQaB1Jq+PZWVRkPsC5raLsZiSVJS4fbdjA54+8VFqqqGjiM07g1o6/bNsnlkXXDKd3S5RrVH+JlvojsZCC64cJBJwMntz70MNTW0T0QTLR8Sprz20k8OI55Seh6GvMutvFa83e6PuoS9FhvsBmZapJUW1qHG5vsD068jBPfis2/UwbdigzZMTyiVNCSnGz6KGRjOcAn+VeS13x8WS2o4tTBzO5HP1G2MEWyCnCy9V3fV4m2ZMu3wv1jCcRn9gQo/Ug9O/Ws/jWG3rfduVtuDsO4JOCpoEKZUedjjftWc6U1LNsknzWd8ZWz9pGUsHeCQeTxkZHsDzVs1XqZCIrlztqkw7wpnejaMF9A5KDnqsckfQjvUfhnFG8cY95FpG7jcZx9e69lpKWoaWhowqr4j6ibukyPbrj5dpvqVZj3BsZjvq7Ak9Cfaqem9TNSNvwLjF2yGgrdNQlSG8Y6EcZB9vnV2uS9P8AiKwtN8gKeZdZSRc7a8FJJxwXG8gpUPfFZ/rFqTarFHtwflyHlARGXFDc4AvbkHHJISk888qTUrinFGUULYH2dK/Deo5Z6jp4WQamVseWYvy5ePcqVdrOvUUhcOGw48sPNrQyhGFqSOMgFOVEp54P/LBJNqMp1y4XyamPJeUVfBt+t4ZPQjojHTBIIx0r0t4g65unhtp9uLbpIYuLje0uvFK3UAY5GU5ycdevb3rygxb3ZK1LU+kuKVuAXn1H5VT8Pp2xB+h+L22z3i/jvhUANzdMvLt4b2hgIbHqys7ln69vwFdqatgaCzKUy9jj07hx8qD/AFG+sJKnwhsnapQCj/Km0LTtrjvILjxkvf4lYCcdelXFwBe6mssiLVY5M2ShhT6UOKG4pQnlI+p6fnV1t1kas6FbCpa+m5xZUTS3TljizFLkRnHkOxyQ4kr5BxwT8jzzTN65rt69shBLQOCoDkfUVn6uqkL9EDvHr9V618O8Cp+z7fiEVzf5bm4t1sD16nyXxURJKsjscUIuJ9oHpmmDE+NKGW1hWDyAeRUoaSrOORVTrc3BXsMbmnISj4A56cVMzB3CmyGASBj5UQ1E5470x05sprQEvjwdvb60c3ByOmfamTEQcZ6/PpRSY4SAMVCfObowsENb45xginEdtCQCaUS7mxbElx1xKAOpJqsXbWbs0Kbh5Q2eN+Dk/ShNp5ag3bt1QJqpkTclWbUOsYdlbKUHzX+iW0/zND6G0rP13c231t79yvtcgoBOAE+57DHelGiNBS9W3VDaWi4Ori1Zwke5r3j4DeHOnPDq0R9R6kfYj29vamAlxQ3POnI37Rz2wkdMkn2NaPhVHG+oEEW/+zjsAvOuPcTdHCZH7ch1KfeH3gW1D05EhPNOIQ2jc4htY+0ccqX8vmea2C06Us+lLR5shTSI7LZc8lPIKR3PQr/IfWqPqrxtmESYWnreiEY6kJJlABat3I2gZSOM88/dVHvuupezUa9k1ttDQU5MWPNQg+nk85IznivVWTwUwDaduo9V4rJHNUEvnNr8loevfEKffdPsI0+lUXz0LWlpWEqcQggFJI6cHoKq+qdOi+2+0Q22sRXoig8oKyW1EApP0BqK1XOV+qbTKkSLdLU7b33Yr7ALRG4cZSfc01ssmW6nTq5UB6EQlSkrcWFhYxgBWPpUWWpc53acwfREZEGjT3JNoqGuwy5Wn5KllTbIU2tznc4DkAU+um+06xnXohYS3bS7+y5UVBGRgdqDs81u8321TJUOSxKSSwsLbKUqKckc/TjP0pVc9bxNNu3KdLkCKw5GU2DKSrDZCNoPTnJV29qbJM0Sa2Y1DI7+f7pzInObpdy2/Codl1B/tFGMzlXmEnJVknmpJYwdw6dxSzTsi3O21C7ZOamRwrapTSCkA9e/Wma1ZUk5zmvRaeQSRBzDcLISsLJC12CgXE4PHShlp3KzRT6tjhyPSeDQbuULIAyOxqYEArU9vklAV3T0H8akbkn1fccUKskrQonJIxmpGlhByR6TUTkjJszw4tBHUbhRDDgDeMdKCSSHWlZzxg0HqDUsDSlucmXFwtsbtgwOSeePYfeaiTzR08bppnBrWi5J2CeF3qbV9u0s4ymet1kPJJQ4hGU8cfaPGelee9Y65n395cd+WxPjRlLLUpeGgUd1BRTtxjBIBxkdaF1Xr2beHVw27q6+PMKmkSNrjiADwdm7GTnuKz6+W18JeVdL6WEH1ON3CQGmT0x+zyB+A+lfN3HfiGfjkxjBLIhsM3PW+k2cLi4uMJC62ylna00+wtK2Z0+S4hXpat6F7QfmVEJI+mR9aCPiY44NidNCTnHD0ond7ZARgH6Ujk6s0ohIU1585xH2kQmcNDAwfWvb9xCTmkknXl2nv/D2GI3ZGVcBxB8ySfmXCBtPzSE496qI6EPHzRnxcbfaxQu0PVaLB8SI1slMRdRW5uwoWseW4uX5hbScY3NYC9vz5x2FaxpKVcpaDLtEyyXyyyFF1EcqyAQOAlC9q0r75GMHPvmvGUu3iS+lhp0y33XB5sgnO9RPACs8jPU962Dwyu4sTSYSn31SE+aiIIqS4h18gJwpX7qQCcq6feauqamFC4TU/wCo2uM2+97Z696kwTujdcL0K9b4ctbjbdtVCdylx9K2wha+CcEfXI75wkjHNVFxEBjWkWXL81TUNYQgtHkJSMLV/vEY65ICT2AJ+ktWxnLbLkToFwYuzSfhlNx1JX+05ASoKOEgqyRgkY+VV0aTSqXcYbmqnGZr43MKWhQaXxy24oAD5BwZHHIA6Z+WGsn4kayqk5iwvnwHdjfpvZSnO7Q6rLKP0i7ujWOuXp8ZpLcZbQwwyyGkMgZG3/ET1KjySaxpqxOrJbCg24F7Uq6+rtn7uteirpou8NzmbXNt6YzpX6Fu/ZIOMEKGQrOPtDjpTp+1af0q2P2TAcccQ43uSXFrUgkJKUjKiB6jnp6hnAFaGXi8dKAACXHkFHsea88WW0Xaa85CctqlNk7XHVJSGikY5ClDryPnVlZt7EGIuLCgNR3Sna8A2Cvr0z1P8K1F64RA2FJhOR2nPQgyGt6kn7R2oSVZUTk5J4wOnZZLlWqwOMOvQltxNpKnpDqAvH+XPy7ZPI6UKTjLpm6YmEOPf9EeMXKzqJCe0xc03UR3PgXE+VMbHICPf7jg1ef/AIejVMJbsF5BfA5SfsqHUKSfY8cGmmifFXTd/wBTTLRPshTb1sqTGC/S7IXxt49ueRg1FY0ztGahuZjrZTam5ThbZC9yWQcHaD7c4x86jz01e+mNWG6Xt87jv9/Zet/DnEXMY2lqtv8AU/j9lkt+0HcrDJxIivR1jo4kYB56gjilAfu8NXofLiR2WAfzr2xGjRdVWhEhltCg4nJacAVg+1YzrzwvuMSU49FtyVsHn9gjAH3dqqqD4ibUO7GpaA4df5/dejizMsKxuPqicydr0ZCsd84o6PrB0kAxRn2C+34U6c0a4VbVtFpXcKFR/wCyUhCgpLZG3nIScEd6vTNSv5BSmzSgboNzVcwkJDCE9gTkjPz+VQO3a8ThsRlPOPSOfpVphaLmXEtfDxVvHpynr8jWy+H/AIRvtutPyYnk5AyFgFX05FVFXxOjoWayBdK6oda7nWXnW36Avt+khAjSX1n93aVH7j7Vreif0dJboQ9dwIjQ/wDDSQpZH8BXqSxaHRDi+Y6G48dPOVYSBVI13r2BapPkN/sba2hTjz6DlxaUjkIHvnA74zWbpeN8R+IaoUPDmBt9zyA632WeqOJwRanXuBz/AB4+yv2iNOWKyyhCQ2lmHFG99KFBC3Dg7UlR7kjFWC4NOakZtmlo7yHXWiq4vQ5oKQlGPSdwPRGD+NVGz6mH6obsVyLaFy3PjVqeJRsZCcoTu6naBnHXp2pguc9erXNkMXX4GbdX/hmoy2fMQEAetK8p3BKgAAOOnWvcuHcMZwuERtNzzJ3J6/dea19c/iEut2ByHQe91ZLgy6w7aXCu5uSX3zJeDBDrSyDjywk44wOPrU15vcv9UzZdsdg283aclhEea2ULwEhSgoj94EjgUt0JPZkJvd5sjDDDFraTaYslqQUhx1SPWfLPRYIUQo5yMU1XBjC+WJQun6xiWlgypTGG1IccPK1KPATtAx07VbsaBYO6KpeTm3VWaJFYiW6W55SXUswURU7F7sLWOQkK78H8Kd3BURmHa0L825JZjlxxhsBSY425wcGqXatSWxLlqZfvDsybeHVT/g48fILCVkZOEk8DcM5wcUF4kamiuu3d2329Xw69tvEphQbcBI9Xp7bSFZzj69Mht/q5cRzCa2+7vRHYkyS27MZlR1upaLvpb25TgoIGD6eo++jrxHRdbdKt7p3zZjCWAzcW/MbbKdpwAOcnaeM44pTpOwWjT6w1CnPygmO03JcceW+gKWEqWrKtwyTxgHA++gL1rm56c0TMv8SW1PaZVlLYSpL0pz1EY2AlWARnj8Kbax+TKX9W+EuXpaVppx1qUmHHQkBSUskJCfljPehFK2ggDCT29qzPR/j3ZvEcfAzLa5AvJUVeYCoAKGB68+4A+nStFju+YgJJ6DAr0ThEj5Ke0gIINsrI8QY1k3ym9+i+rIUkhR5NBl8oO0gHHyqZwlJ2q4+fvUCnloPCSoHnIxV6FVlaOHVbBk5CTmp1KOAM8ZoFh0ONbQOe5olpZWjB44xQEQJpEeSrYVnjof6/OsX/AEh9eOWRCbd8SGYWwLeS2CXFr5KUDjknAI5GOprXITmcpz8/6/Os7vOgos7Wki93zTsrUuXUmGyw60WWwMD1oWpOVHAznIwAPfOR+JKWorKQUsBIDyNRF8NGTgZN8CyKxpkw364Xj+96yeuW9plQs9vQfWWVbnnTn++ME/dge5NVUiDJUPNKYrCTnC8rdcPucA4/hz1Ne3NUeGujlaUW5dtLKtTBnquDzTlwSHZb58wJSVISoqHrJCRgDP1zgeqPCmPMXdbowyILkp9IgW1QyWkbkgA4GeE8AY+7pXmdRwf+3AMafwfrn18sKPLEWbkHzWWIWy+2EoQmDbWzzvGXHD2Jx+SR9+etdvzvjcxYpTEhHhxbuErc/wAxycd+B1+daNO8IJ1rbiIkMhpzatXnyF7EpSMckEnaPZJGST35Iis3hk4xYWp80IMwuDy4rwKQ31GXM4xzj34/A0r2Obc6Tj3z333P4TQwqoaTtbMvVMBkj/uqnCPUduUhJ/AnkitsbH6i2fDspZYQcoKGQQB025Ss857Hb7kdTXzRmgFNPPoBaeZiv/EJkNcrV+z27U+kc8qJ/wBcGoNV3p+1xpMl1tkTG0gIPkhCULXwgAZPq6qOTkJSffiq4q57Y2U43fv1/wCLqXG2yVteO64GtxDuFthXOEzuLSJHmbEvpSdpUUKBUkEYI6HHtxVK1Z4yakvN32W62RmG1qyUW5kbS4T9lsKJJAozQnhe7rm9xYgeXBblBSP1gplTrSAEZWVEdMDJ6jqOea0e1eElp0BqITmmG7i5FiKRaWUoebfPOHH3d/pQkcAKScndjJ72baOJtG17mAtYCRcHPv6b8lcQwgxmRwVek+JOofDjRrx1OWr3JcSRFtxQEqgq2kk7wMk46pHHA55AqhWjxFuc1Mi4NWWDPdd/tZbbqy6k443JWpWMdgAE8ccVY9aBF5uPnyXkzWIkVTythyg5ySlPyISnp168mqK1Y1wVx37Y65GnjlQbQdgHsc8Kz7c1WxOhqY9UrBqPjYDcDBB8/UclBeS43KbSNRRJjnmXG5XBuUP/AAZTpaQnuduzCO3yqi3+6Im3JLkJhXlpTt813kqHvg84+v4U2ud3RMYDMqKmM+slKluElK/mM9Ce+fzzSdmxl9klCstg7th6fLj7yatKaJkPzOx9vK1k5hyrd4Z3SQu+CRLuMBSfMCnGpSElR65KON270jG33FQjxAcsmr5iHXk2KG4djDc5BW24E8J3Ec4P95PKc96qxhiI+0tyKlZQfsk4Sfbkc/8ASl2op8e/Jeafdb+OZAU6pYIQ20MYOOhOT/AAE1fQTA208lueEytmBieLm3r58l6T0fLiPz2XJqbhYC/tKXogEhheepSpJT6e/c+5rfrR4V6tusMO2e4i6M7QsZRuVtPTIUBj8TXgWz6yvmjWmX7NcnG4ClftYM5BX5iyeDgjByO46V6f8IP0xb9ovazMaKXlgBTKG0vI2gZJ2rW2oAf5zUaah4RXO/76AXPNuD6LU9lxKBuuhluP/Vwv9Vqsrwh1UHR8ZZ21r7q+D3fmkmmNq8IL2duLEyCe4iLo62/9oNY30lLwtgdABIfXJZVz09DbT3PyzVoh/psWWYlwBNlC2wCoG4yhgkZAwYgOfljPyqI/4Q+HpMNqHtHTCT+6ceaPmp2nvyurJ4MX54AqjsxE/Ngj+dWqN4L3FLY/+YOIV3KW0tj8go0tT+k61MWluO9Y0uqb83ym3pDzoRnG4oU01xkEZzyazzxH/Se1U1aXH7OxIKA6ptT7kb4JgJScKV/4qyPbC0E4oY+D/hWAapS+TxP7KAZ+O1j7BrW+X7ojxWhWvRLbq7lOVLXFT5kgzHfLZbGONxUf9K8Ka08TVa/158QwmRNjRUlLb8bLLbLaTnCUnkAkDryePpU3iFqzVXiHrhb2oFLXAW5vissy/QGycB1SMnOcEgqyqqNYJFrut2k2y03RbLDqimSzJPkzHQkk5bVgA9OAT3/C3ppKShj7GghEcYybD7nJ9eauBwiUjXVSa3nA5Af/ACMb9ei1xXiYJTbRduUaWi4ugqZn7U/DxiraclW1QUSD0yMJHFXqV4kWq33aZdrTDksQ2EJtMd9ooAcX5ZG8IyCtXqUcjpuBz0rDluOQGDe3bW9bpshtcKH8fHKtvISrooq4STg4wCe1T2Ry12vUNttEu7Mx4NqdUuQ5GmLbytWPNBB4PKUpByOlaCKRk1s4G/vPgs9UQPp74ydvePFepnPEC1afhWKK/IkMx2EC43BssLdKm+dyVISlXO37wM880phatav8C4XZL0Bar2+qL5kRakOOMggqIK1YRg4BG33FYVC1pchMnxYr1xaVen/KhufDofERneM/bVu2HkZJxwrAFXG2XmJFTJmxkxbpHiNuw7eVMLZUqRnC1r428HcQMf3flU1pazF/fu3oqJ4cTey9BWPVzGirnqK+RYEm5QLXCTGZWpSHA1z1StSySDtJKR3UPcVULL5uqblaJl6lWtMGY4q5TWQHUOlClADjdzlKEnChxntVdsaL1ZtP2yxz7OzBg3BsS5EmDOIcaUdpytsISc4APGcbOfmE/qO6WBm+SPN2KnJVBt6pzS1JHqSVftCpS1YRkZAAysZPSmtZvpOff8oLndQtkgafVMlyFwYjEGFepD6lG1Sy044yhtYbVlBA6rbGByeOeK61FaLtIkwo0DVUa0SGWiGoobBKySAUKGTuOxtSspxye4rPPD6NfI9zDBDRREnNxmZFtkBCG0b8POKb3KGS8raM4yltWR6at/61m2O1uItMmJqtKZRdadK0iStpAUlJORhagVLOR2z1NRHh1+R9++aMCFjOotPXxet4Mqe3bEaiUkrfeijy1y2QpSQtaQACo91Y7Vr0AqRGaChg4yD/ACqiaV00qbrK5aonlxx6UlLLYccUspSCTk57ndjjjitEdbbS2NhwOxr0vhsLoaZjX7rFVsokmc5uy4fV5jRV2H5UMVuJJ2Hj5kV3nKCAoAkYx7/KhwQMjYFYOPV1q1UG60CKry1kHpkGjGVnKc4xk5pc055igT1ORRiFEhKk4wQM1HKeEYwpLTpOehzj5Zpdf9QT7e6G4kUOpwVKcV0T36Dk/dRCVHeofaPX8q5v11i6dssu7zVbY0WOp9xQ6lKRkY+Z6fWgyENBc44CI3ZY7c/GFV6mPpdhspZaCgJDyDvSO5QgHI59ynPFVB/Xa7jcfhdMW9yVcUow88+kJUynuSvGEj6YPHeqJMkXHxL1FKu8+S4fi3d4ajqUMJ7NoA7AYGTxwTycmtEh6KvVuTHtkR1m1IUnzPLbSXHFg8blZI5+ZOenyrx6aolrpHSbi+9vTw+pQTk3QcNEh2c238Uq+Xj7S3lJV5URPdSAEqzjspXJ7Y6VYk25vUj6W/g0IiR8b5RWSpX+FPP2vuwM/cRtSy7b4WW1Lk9Uy4TFbUJjNq8x+WsjKU+yQACeOEjJ5NO9I6kZXZ4Ny1NLiWtLpLoiMtD9ik/ZQD1xj1KUT1V7U5vD5HMc/TYDf3k+KlRxySGzU3+Hg2y3sttiHD85vy48V4nBxkkYAJ9yT+JrDdUqka31NE0/AKnFv+a8Ftp3FxxSCEZGeBtScDslY69K0tF0sd/tV8u9tduE6AkmLGffWVqeWTnag9VpHTHGTxg030K8nwUtLcu8QIbmqrhLcliU8ttSYI/smkIBIBX5ZITngbzntVDJwozcUZ2ww1ox3k4HcSLnuGTbCs20wvpYb2yTy9/dP3o8LwD0NGK7m/DvM6QlSLY48UMkqaCP2jfT91O7PGSAT6VJqLUVjE/w2dv+opqLpLkfsrexGbbQp85O9HmJSFFlJ28AhPpxgZBrm1WNF11M3qPV/lPOuhTcO2PLDiynBO49Tk91cBI4HYVd5ej2/FW/x7hcU74Fp8tLbMdflsN4ByhvgekBIJKvl8hW6qaOn7B0TrC/6iBgdwG1/LvPRTWl7xpbtsB1715GujLjbNwYcQlx7y0hwpQQBjsMcAY470jvzzsJ6IqOfSWl+a22rBcGUkg/w/HsTWk6w0za9aa8lWTSBVLJnOqlynTtGASoJJGfSgBAHUlR91ACi3SOtuD5ryiVxylBaQgbuTgn5nufp0rx2SmfSSaXjmfsPwR0VfJGWEgqjXQNXR9txZylAOdyQkJPHp56kf1mgInxEMrKXnFsb1KwMbk88EZHIx2P3URdZAj3FaWEBtCE7XCUc56nqM8Afxodm4tsTEJSsusv4CiEkkEj2Pt+NWzQ4MsBjohhfnlPONpUhSJLOB6sAK/r7hS2VHhyWVtSG0rbJBUy50XjseePbNOZ8ffIQuM4psrTwpBBSofTpSeW7s9EhrGM+pA4z/KjRHYt/lT6aUxODhySyzTZtnl/GOW9D09SfIbREfSkR0HkOBJSlIHBAyeoqwlkaci/DyZUt+fJaLz0hTCXQG+TsBCVpJz6lHPH5UJB8j4xt11lha0EbFvshe09jnB6ZPSmln08xBVOelpVa9OQ1pmPzG5KgJKiMBGCc8YODkYPODkVMkex+SLH79ANz5DcnOy9X4RxA1LA0nI35ef88rKNOm5EW4IkmXboPmbf1Z58d5TjyioFS1JCTknCsAA4xgYHNaBppuNa4sZj9Vr1DeXFl1aIzqG3FbsFK1ZcKkDn0jPY5FSwfhnG3ZseyxFXByOuUzBdfAABSAjJKgADjcTxhO7kZ5aaSuUe/PRtgtLiHy4LhOjMbct4O4Nq4UE8EblKHXHA5NW+eQi5b+nw92+527tZeNvytO/v33K7aYjxbHY7lOkKnwFna482icZa2yB9hClBXBzgbSf3gMc5IevbuoYCQthL0u35cjxor5acjoUnhSwsNAE5PUY7ZOa5t16sfx0cWhxLkVAcYS9GG1mIlI9ZKucq9OfV1xn2pDeIEG62iRFt1/Raok2R8LJmT2g1Ied3Zb2OYSkKSRkDvu5UkkGqt8s73FpJAx5Dy9/dSWPiYLgXPv37sqvqhiTdLB8JIkIfcjbkzpNze+Hce3AFsNvICgR14BJODx6c0gn6QBtUJ+cFhuQ6GocGfC3uOuJbAUpTqBuOTzv4z0CemNKsOmULjBrUy13SxRyhqFFu8YuLdkD0hanFK3HJ6jalI3Y5ySBJS37ZO/VEWzzDNlAIfXYloEeOApIUj1gISfSEnBwMEZHOTMke06Wcjf3+emwumyStdlwWeXqPKUzbPIVJTeFhMFhiG4h1iGEAY2pXtWSrryMg9STgCvzYsqPYp9tCLa66Xi5dp0xkxhjP7NBCD6txCiSk8nGcbCa02+StNpEq6wkQ/wBWI322ZPRFHmvqKCCELbG5snBBJB4VnBHFZHLiSnXkvz3JbNjjqRmEsfGxXk558talEng4KkZUkKGfatPwxz+WPe3vxKy3FXMcPmz739+C+hLYixfhNHR51ymo+ESlh1SsMJSdykLO4Iz/AHs9NxzzV48ItZRtSRo9oU7LstqsqA7JjuNh9Ssq+152Mc44xztSMdKrESRddOzYrT0y1MvTD5b7MQqjJgNKACcHeAlZTzsCMgH6gM9QXpGj7WxMh265s2SCWw/cUyy6mVvVgBJKiM8c7cZwc8AE6xpLsbnxv79lYeZoHcPRWJ+5/wC0+qLgld0iyU3MNx7YZLjjUhiOklRBUpvy95KUgcZO488gVo+nZV5twQmNJ8qNaUfDOhTK5AfG5brjocQUngBSThJyoJwTkkZTZtdzbk8TE1C0/JfWZLUSfFbQWxuKVqUrBKTwUpIUBhORgAA3+IyuTcpMSXarpHbYhBxcqzvlx5z7KlBxxOSncUjCAfSPpwR2RsoNrHdXzXutXNCaSL9zjxmk3B9v4ZpI/cJyoJQdoTtBAJyMbyevNZRrnxyXAULtaZSQUkNOR3ICjhQwAUrCiUBIBBJOMDrk1ertqaNZNN2tKGrrqx2FAQ6pko3lIUtzJWCrI4yMkZII6YNY/CeleJMl64SbQIUSf/bwHQoFpQOOAeR0BIPPJNS6Cj/qpLOvbn73USqqOwbdtrrcNDX525WhE52S3LMnC3FNjA3Hk8e/erP8fsB/fQrkfOqBpGxt6Ut6Y0ckMgYCSeKsCZnlkIzx2zx/Rr0ONmhoaOSxrzqcSnzjvOcgnGeP3hURlNu4UtHmnH2hS1q4BGQTlJ9uoNQqnpaUUlLmfdCMg/OpAuhLV2VEKA6YI60WhxTaU7FYyACB06/8hQq0JRznlQ/A9aIaIJyOABn781HJRAFMhR8xa08gDdg+3/TNR35pq6WQRZLTbzLiQhaHUJWlXORlJyD24PtXK1gunsAMD6Vw6SuMW1K+yo/T+uR+FCeA4WKIF5utWlLj4ZaovU+/hsRnX1rgfAMqQztKsjnOEYBCdvbGQelGXjxnBkl4pBW76d6jz0Hft3r0CQzIioiymkutryCFjI9v5CqdrDwP0/rKOwryPhlIBA8n05PscVWR0MEA0xNsCb+aIMbBecdW+KFxvLiXW5KAgr9SMDJwNv8AL8KV6jv0/WlvixGZioTSVpbd2gEOAcD6DHXrmtT1h+i4gBo2mSptWcqC+ciq6PAa82eA66hzzHEZwnHU4yP50UwG2CuEhBynsPWkOxaWtEVKS6u3oyww2ltCN3IJ2pAGR1HHXnsKpnhzKvuu9eGfqNao9ptshT0eOAUl1Z/eJ7jgYHTgUml6cvcNwLfjOJSFA4GfrVli/rKFb1BCHUjPX5EYz/XvUb+iYH69Avvfvtb7YRmzkAgHdX+w6rPiFreXOBVZrLCwJTodUgyynogZAO0DGcAewo5jxB1V4kXqZp/RExFt0slpLT9zDaiUHdhSUAgAL4OeuAoc5AxmkK2XKW0IjjSvhXzhQA4OTkZH1/jWu6AhxPDfTqItrhtx4+Cvym0jBJOT/Ggf29g0gi7Ry6nqeqlNq3G/Xr+E4CHvDy1qLiExYEJrDD0laXJD7uSPNVnPHAxnnnke2ZwIidc2l2MYiG23JKZLkwowtYG4qSDnHPTpgbRjHdne13fxR1AwZSFMWdhRUWu7g7g1Zr/Ki2GAzHhJQhRBRhA+wMd/oQaHJwullBErLk3yd8kH8DyFkjpnO8AvPt98NmjPvUl2QG2lNrDaQPsP9yflnP5+1V+b4WXh+xwpbBAnyF4biDjzBjGwn3IGQe2cVqglHVF/XFDYEaIsF7B4WoBWefpvP30Zf2k3e4NRkZEdhPmEnt2Jx8ldvmaDJwekeS4MsSD6nN/Ll0QhvlYS/bYqNOSHVTm40tvc4q3O5AUkbQO/CySccdhzVWgahbuN0Xb2mHXSE+k/aJ6Z6fX51c9dQ44cuQQkqeS6Ut478cf18jVdtMd3SWjLrMeCGJDqktJfQj9okHqCewx9+eKoKvgMEEOoXJtbpnqrCmDJZA04C+3KImxBv4p9przU7wlTqTgexGeDTHQK4Opb1CfE0IiQV+Y8rjY4OvlkHHKgCMexPBrGJDzmpX2k+Yos+Zgubsqz7JyDyfp3q43p6FpTTzMxzyEuJ9EWGhe5tHQnA/eJwCpX3ZrNPoOzAaSdZ5e/YWxpYIYZhMwnSO/fqtX1Tf4Ee3zJ82I+pp5Z+2UpXM28YJBz5Y/u7cHr7VXk65m6o8uw20ptqlje61FR5bDKB1KugAAGSTk8CsZ/25umrZ7a5bhkvkhDaUpACR2SPYVuembfa9NMIjz5gZW84h6alvCd4Odsfg4wT9o/8qfJQf07AJBlaCKu/qHHszj0VisM1Njjw1+ctTDa1PMJlq2yZy8FP/6bJPbqrp9LMxJvMtLx1ImPfZTjzSodjZc8tbAV0wEKCm09Dggn65OKPctT2+0IfujLzEu8TXNjRawRBZGU7kA5CT6Rj2/hF/8AESDYWnDBaUZsnKZU1pwqkOZ64WeEjkjgZxUE0Ln/ADBu6sm1bG/K4rW7j4goVquTaWZc2HObbSlMkBbrDCgPU2htvH2RxvKCOAQOpo+Khp117zYMT9QlsGTPVH8tctzlW7CNpyVnhBAI645KqqmkLvZ47Ls66OphRWmd3wbaPXJP91SzyQTtBxgH7zVL1fctQeI1+YamzWLdaHHP+7sXEBtCVfu7U9c9MHFCj4cC4tta3v1R5KvS0OGb7D9+5Xy4IuWp5cZswWrHZoixsi3GP570jdnJSrcVJcwlIJJICSPYClDtvjwHH4sKJFl6ma2MLRHU4gxmichSUq+ySScnpnk5yKA1J4jW/QGnU2uy3R243ZwL8+SlZUkKV2bB4HU89eKcaYuMbSmnEXLUceFaXZTKVOLShSpMgdQHM5x9568GrGKnMbQ4jHLv/KrpZWyOLAcjJPT8JVfy89YXbU3bZHnOI/8AmNxXKQ2VISnhYcOc4ynJACuRxzxUBYpt2kQ2Ldc5Ns09a05kTHlONurUrbvWcgBRUUgAgnAT8jT12fA1zOYMScLPbXQt6Rudd86QSTwreNmDjoeBk9xxYdMaAlybtC8422E0wsON2tUgqKEZzvcVzv7EDOFHbgAJq8jvC3J8iszO0TOOkX7wi9OORJVmk2SPOWW5bqI4kpdakOvLxhDBJQk8AEndux054pnqqVPtZlQ7Ne4iQ03/AN5VEaS04qQkBW0qUdqwpWckgYwAOOKZ6pZt2kbDPXbVqut5WdzTy220JZWftrSSOpwOeSMDFeZ7m9Khi7PM6h2sLVuQ3OaWkocHdK0IKVHA4J25GOKOwdoLhV0rez3Vw1R463+XLTaLvIEaREZS4LjAc9RJ2lQGUggfa7e3atW8O44NuEtT5lCarzlLWcqKj3z714j0nbNQat1mw/OdkzY2/etTp52e30r25pVtNshMRsjy9g2EdCPf+v8AStpw2iNM0lwyVk6+pE7hp2CvJUEBSeOvXsaiV6xk54O3Hsf671C1I81s5wlSeMn+unzrou5CgB6kDCk/L+u9XwVOV9bcyCT9sfaSe/zqVchROAEnHHJxQZUVhK08BQ4P+tTB8NAAkAnk5GaIhrYnHySEc44OPyopt3DJ+Y/r+FLArLo3Z6Ag/fRbSypngg7T+PNRSihEuugPcn0kdfqK4Us8jooZ4+fahnkKKkqJzxiu2fUVe/2qZZPX6S5uZQsdz+HTNRQ7stkFCzkbhmvziiUFIAHJOKCfSCtagMZGabZOun5uKXmkrPODsz7HtRCQzJQQcc5I4+/+FVZD6glad3pOFYqaDcVtSUqUoYSQTnpjOKTThdfKluWmIcyIpIbScFQzjqBSOPp6E4FR3GkoUU8DHcdqsSLiFPONjgBXT3H9fxpHdnShQKThaVdR37GkyusFNH0lGbbTtSCCM4A6fP8AHH419csyUMH7J2KCgfYHj+OK4teoluMkK6oJGMffRcqcVo4+yoFJP1/r8qYbogsEBEjMwkrCcNJPq3Dt/R4++q/cbdEntrUtzyzJSW8k9D8vvBqe8zVAJG4JJCkkZ9xmsw1FeZkd+TGQpSUkhxHPQ9/5H8aaGkp2oc0cxYzpW2y3WjukB3eT9CDj/wDiB99V1y+/q5t+SpO5DvqT8gQQR+X50kuetJaGg64SXCny1Z/ewR/oD91VeZq7y7YU8LbCiQk9s9vyFKWFKHBKLmh27X454bH2VE9VDkH8KpXinFelx2IHxT7LDai55IUdij9PlTq46qXGeDkf7JwSPmP+tJdW3r9fR0rUnDqRkke/9c0vZh36gl7S2xWQSEzrc+EMvq2tq3oB6A+9Lnmpk91AfWVJRwASTVxmRg8MkYUjoR7UO3DBVkior6WIO1acqdFUSkadWEPaWRDQCgbSOcjrTH9YPPEhTi9uc8HpUrEQJJBFTfBtq6px9KgyU2o7K2hmDBkriPOUg5Dqjjsev301iTnVYWSQkdgcZoJu1oCgRmmCIqigJ28d8VHdSgbhTGVF9imT2o5NwaEfzVpbzyAf5Dill3lP72ozfLYBUXy4rfu9sdMVL8OmOjIGFUGkF1whOSffFNbTRtthOkmlfc3wtH8KnbDbYz11vd2ZVcWs+Wy6OWkAj7JPBWrt7daWTNZXbxhvP6kjITGhCT5hcC/stJJ43H2HJJ5zWdXlXwzW3G5fuaCs86XZW3nmZDrBeSUKLaiMpP8A0pg4cNbpAbuO1+Xgmv4kdDYnCzRvbn4rfNC33Rr85UR5AkxLa7shxthW7NdHBcUo9E9cJ47114s3qFpS3X1+JcVvasuklLj0hk7UQW9v9klWeyev14FZXbNUG3aMVGg29uPJUol+5hR814ZyEgn7I98daze5XO5azurURLqltBWEpH2U9ifypjaF7pSCMd/v3zQZK5jYgQc9ysOnfGu822QthqYuVGSrc4H1b0q4wcZH1q4Ma2smoHWTZ0vRrm44kLYCyUBJ+1j36k8mqSfDlEZTLDafUeFfM1rnhx4XxLUpuahkCRjof9auo+GRscHD/lUUte97bFaXonSLFqjp2spDxT0GM/13H9Yu8UJjJ2AYSnkcfY+n9fw5AtKUtxAgKORykntz0/r503dShyKHW9weTwtJHX+uv4/feNFlTuyj40lQxzhQ9/4GjBIAcQ6jPXYQe2expIw5gbgobRj8PamTJwVAKB4/Gi2Qro47Ep3pH7FX2xj7NQuzEx17FpCiBwonqK/MSNh5wWl8HPY10poxsJLfmJIynKckD2P0pQmrX/jAsoT7UQ28kNKxxyTSQuqD6PkT0o4ZEZtZPCiRUYhHCYrdStKk5KduPw/oVHHeJUn/ABJKTQaHMON988H512z6FcdQsimWS80SpwqXxkE+quXEhWw/I5r8k+tPtmpBt2NDB4WU/d1pEqCUyfNCRySMDFBuA7R7ng/T+hTIk+lQOClVByGwppLnfeoGuC4oZbympGc4UU/j/WKgnvGQ6c8FQz9/9A0W6gZQccioZLaSyvHB+0PlTrBNyloSphxxSQc5BP1BopC3FILRJx2oyOgPLSo90gn8MV9dYAeUO4ptglCrN0iLeS44DhScYT8xyP51ULraDLjh4g70D8R/0rSZkUbVn2wr8/8AmaWSISG3TgDHUj60uy5ZNetHIlrKMApcT5qfu4P8qyzUOiJDSX2U5yg9B/XtivTb9sbSGFd2HyhJ/wAKhyPyFV/Uem2TJLiQEk5yPfH/ACojSNimEHcLyfM0vJSo8HpSt6wSggnaa9I3DSbAcWQE7QN2Mf170nVpKP5qm/Scgkce1Hs1MF15xk2N1BJKDQhtxQcEVvdy0hHSSPSe3Sqlc9JMtunBGOooT2AqdE6yzdEI8Hrip0wie1W1ywIbwdwqM2lKMYIqIWWViHahdIWYu3GRR0aKFZB5o8W8DPNTssBAoLmKSxyHFuZIAUkE/Oo129tAIQgJJ70yKfSDntXSWgpJ+XNNEQRHS3wFVZmkkyjucUcJ7UC5pJya82hIw0k8/Sr6pkFup4rKUp3AAVKZE2yrJpCsy1VZZCo6YUcqDQGClI6008PNAG2n4qQ2UKzwMc1e2bU0+/uUByasMqKGY6Wk4GOMgfKpPZNHzKuMrjhUlMNLt2QQgEhWRx1FaxY4uyKhY4B6HGMnvVKiwymahJKc4ByK0azthMDIyUnkJPuKUBNJRzTSmSXEpwrOdo4BprHUFAKb+woBSCePqk0tW+FNpeCdpwCQO/WiI7+XVMjISAF8/P8A60+yGVI+2YD6Fj1NqPI/lUsWUOEk5yfSojtXxz9qztX6gvIHPIOM0tacUl4sk+pBJz2ogFwmFWJLwO5vOeeMdPrUqJDrKQkhS8dFDPNLoz6vT0yk80R5izyhWAecGkSL/9k=";
            public PointOfSaleSettings()
            {
                Title = "Tea shop";
                Currency = "USD";
                Template =
                    "green tea:\n" +
                    "  price: 1\n" +
                    "  title: Green Tea\n" +
                    "  description:  Lovely, fresh and tender, Meng Ding Gan Lu ('sweet dew') is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan where it has been cultivated for over a thousand years.\n" +
                    "  image: "+posDefaultImage+"\n\n" +
                    "black tea:\n" +
                    "  price: 1\n" +
                    "  title: Black Tea\n" +
                    "  description: Tian Jian Tian Jian means 'heavenly tippy tea' in Chinese, and it describes the finest grade of dark tea. Our Tian Jian dark tea is from Hunan province which is famous for making some of the best dark teas available.\n" +
                    "  image: "+posDefaultImage+"\n\n" +
                    "rooibos:\n" +
                    "  price: 1.2\n" +
                    "  title: Rooibos\n" +
                    "  description: Rooibos is a dramatic red tea made from a South African herb that contains polyphenols and flavonoids. Often called 'African redbush tea', Rooibos herbal tea delights the senses and delivers potential health benefits with each caffeine-free sip.\n" +
                    "  image: "+posDefaultImage+"\n\n" +
                    "pu erh:\n" +
                    "  price: 2\n" +
                    "  title: Pu Erh\n" +
                    "  description: This loose pur-erh tea is produced in Yunnan Province, China. The process in a relatively high humidity environment has mellowed the elemental character of the tea when compared to young Pu-erh.\n" +
                    "  image: "+posDefaultImage+"\n\n" +
                    "herbal tea:\n" +
                    "  price: 1.8\n" +
                    "  title: Herbal Tea\n" +
                    "  description: Chamomile tea is made from the flower heads of the chamomile plant. The medicinal use of chamomile dates back to the ancient Egyptians, Romans and Greeks. Pay us what you want!\n" +
                    "  image: "+posDefaultImage+"\n" +
                    "  custom: true\n\n" +
                    "fruit tea:\n" +
                    "  price: 1.5\n" +
                    "  title: Fruit Tea\n" +
                    "  description: The Tibetan Himalayas, the land is majestic and beautiful—a spiritual place where, despite the perilous environment, many journey seeking enlightenment. Pay us what you want!\n" +
                    "  image: "+posDefaultImage+"\n" +
                    "  inventory: 5\n" +
                    "  custom: true";
                EnableShoppingCart = false;
                ShowCustomAmount = true;
                ShowDiscount = true;
                EnableTips = true;
            }
            public string Title { get; set; }
            public string Currency { get; set; }
            public string Template { get; set; }
            public bool EnableShoppingCart { get; set; }
            public bool ShowCustomAmount { get; set; }
            public bool ShowDiscount { get; set; }
            public bool EnableTips { get; set; }

            public const string BUTTON_TEXT_DEF = "Buy for {0}";
            public string ButtonText { get; set; } = BUTTON_TEXT_DEF;
            public const string CUSTOM_BUTTON_TEXT_DEF = "Pay";
            public string CustomButtonText { get; set; } = CUSTOM_BUTTON_TEXT_DEF;
            public const string CUSTOM_TIP_TEXT_DEF = "Do you want to leave a tip?";
            public string CustomTipText { get; set; } = CUSTOM_TIP_TEXT_DEF;
            public static readonly int[] CUSTOM_TIP_PERCENTAGES_DEF = new int[] { 15, 18, 20 };
            public int[] CustomTipPercentages { get; set; } = CUSTOM_TIP_PERCENTAGES_DEF;


            public string CustomCSSLink { get; set; }
            
            public string EmbeddedCSS { get; set; }
            
            public string Description { get; set; }
            public string NotificationEmail { get; set; }
            public string NotificationUrl { get; set; }
            public bool? RedirectAutomatically { get; set; }
        }

        [HttpGet]
        [Route("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId)
        {
            var app = await GetOwnedApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
          
            var vm = new UpdatePointOfSaleViewModel()
            {
                NotificationEmailWarning = !await IsEmailConfigured(app.StoreDataId),
                Id = appId,
                StoreId = app.StoreDataId,
                Title = settings.Title,
                EnableShoppingCart = settings.EnableShoppingCart,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                EnableTips = settings.EnableTips,
                Currency = settings.Currency,
                Template = settings.Template,
                ButtonText = settings.ButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = settings.CustomButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = settings.CustomTipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomTipPercentages = settings.CustomTipPercentages != null ? string.Join(",", settings.CustomTipPercentages) : string.Join(",", PointOfSaleSettings.CUSTOM_TIP_PERCENTAGES_DEF),
                CustomCSSLink = settings.CustomCSSLink,
                EmbeddedCSS = settings.EmbeddedCSS,
                Description = settings.Description,
                NotificationEmail = settings.NotificationEmail,
                NotificationUrl = settings.NotificationUrl,
                SearchTerm = $"storeid:{app.StoreDataId}",
                RedirectAutomatically = settings.RedirectAutomatically.HasValue? settings.RedirectAutomatically.Value? "true": "false" : "" 
            };
            if (HttpContext?.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash() + $"apps/{appId}/pos";
                var encoder = HtmlEncoder.Default;
                if (settings.ShowCustomAmount)
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"amount\" value=\"100\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example1 = builder.ToString();
                }
                try
                {
                    var items = _AppService.Parse(settings.Template, settings.Currency);
                    var builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\" name=\"choiceKey\" value=\"{items[0].Id}\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example2 = builder.ToString();
                }
                catch { }
                vm.InvoiceUrl = appUrl + "invoices/SkdsDghkdP3D3qkj7bLq3";
            }

            vm.ExampleCallback = "{\n  \"id\":\"SkdsDghkdP3D3qkj7bLq3\",\n  \"url\":\"https://btcpay.example.com/invoice?id=SkdsDghkdP3D3qkj7bLq3\",\n  \"status\":\"paid\",\n  \"price\":10,\n  \"currency\":\"EUR\",\n  \"invoiceTime\":1520373130312,\n  \"expirationTime\":1520374030312,\n  \"currentTime\":1520373179327,\n  \"exceptionStatus\":false,\n  \"buyerFields\":{\n    \"buyerEmail\":\"customer@example.com\",\n    \"buyerNotify\":false\n  },\n  \"paymentSubtotals\": {\n    \"BTC\":114700\n  },\n  \"paymentTotals\": {\n    \"BTC\":118400\n  },\n  \"transactionCurrency\": \"BTC\",\n  \"amountPaid\": \"1025900\",\n  \"exchangeRates\": {\n    \"BTC\": {\n      \"EUR\": 8721.690715789999,\n      \"USD\": 10817.99\n    }\n  }\n}";
            return View(vm);
        }
        [HttpPost]
        [Route("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId, UpdatePointOfSaleViewModel vm)
        {
            if (_currencies.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");
            try
            {
                _AppService.Parse(vm.Template, vm.Currency);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.Template), "Invalid template");
            }
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var app = await GetOwnedApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            app.SetSettings(new PointOfSaleSettings()
            {
                Title = vm.Title,
                EnableShoppingCart = vm.EnableShoppingCart,
                ShowCustomAmount = vm.ShowCustomAmount,
                ShowDiscount = vm.ShowDiscount,
                EnableTips = vm.EnableTips,
                Currency = vm.Currency.ToUpperInvariant(),
                Template = vm.Template,
                ButtonText = vm.ButtonText,
                CustomButtonText = vm.CustomButtonText,
                CustomTipText = vm.CustomTipText,
                CustomTipPercentages = ListSplit(vm.CustomTipPercentages),
                CustomCSSLink = vm.CustomCSSLink,
                NotificationUrl = vm.NotificationUrl,
                NotificationEmail = vm.NotificationEmail,
                Description = vm.Description,
                EmbeddedCSS = vm.EmbeddedCSS,
                RedirectAutomatically = string.IsNullOrEmpty(vm.RedirectAutomatically)? (bool?) null: bool.Parse(vm.RedirectAutomatically)
                
            });
            await _AppService.UpdateOrCreateApp(app);
            StatusMessage = "App updated";
            return RedirectToAction(nameof(UpdatePointOfSale), new { appId });
        }


        private int[] ListSplit(string list, string separator = ",")
        {
            if (string.IsNullOrEmpty(list))
            {
                return Array.Empty<int>();
            } 
            else 
            {
                // Remove all characters except numeric and comma
                Regex charsToDestroy = new Regex(@"[^\d|\" + separator + "]");
                list = charsToDestroy.Replace(list, "");

                return list.Split(separator, System.StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            }
        }
    }
}
