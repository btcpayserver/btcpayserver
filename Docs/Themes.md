# Developing and extending themes

The BTCPay Server user interface is built on a customized version of Bootstrap that supports [CSS custom properties](https://developer.mozilla.org/en-US/docs/Web/CSS/--*).
This allows us to change theme related settings like fonts and colors without affecting the [`bootstrap.css`](#Notes-on-bootstrapcss).
Also we can provide just the relevant customized parts instead of shipping a whole `bootstrap.css` file for each theme.

Take a look at the [predefined themes](../BTCPayServer/wwwroot/main/themes) to get an overview of this approach.

## Modifying existing themes

The custom property definitions in the `:root` selector are divided into several sections, that can be seen as a cascade:

- The first section contains general definitions (i.e. for custom brand and neutral colors).
- The second section defines variables for specific purposes.
  Here you can map the general definitions or create additional ones.
- The third section contains definitions for specific parts of the page, sections or components.
  Here you should try to reuse definitions from above as much as possible to provide a consistent look and feel.

The variables defined in a theme file get used in the [`site.css`](../BTCPayServer/wwwroot/main/site.css) and [`creative.css`](../BTCPayServer/wwwroot/main/bootstrap4-creativestart/creative.css) files.

### Overriding Bootstrap selectors

In addition to the variables you can also provide styles by directly adding CSS selectors to this file.
This can be seen as a last resort in case there is no variable for something you want to change or some minor tweaking.

### Adding theme variables

In general it is a good idea to introduce specific variables for special purposes (like setting the link colors of a specific section).
This allows us to address individual portions of the styles without affecting other parts which might be tight to a general variable.

For cases in which you want to introduce new variables that are used across all themes, add them to the `site.css` file.
This file contains our modifications of the Bootstrap styles.
Refrain from modifying `bootstrap.css` directly – see the [additional notes](#Notes-on-bootstrapcss) for the reasoning behind this.

## Adding a new theme

You should copy one of our predefined themes and change the variables to fit your needs.

To test and play around with the adjustments, you can also use the developer tools of the browser:
Inspect the `<html>` element and modify the variables in the `:root` section of the styles inspector.

## Notes on bootstrap.css

The `bootstrap.css` file itself is generated based on what the original vendor `bootstrap.css` provides.

Right now [Bootstrap](https://getbootstrap.com/docs/4.3/getting-started/theming/) does not use custom properties, but in the future it is likely that they might switch to this approach as well.
Until then we created a build script [in this repo](https://github.com/dennisreimann/btcpayserver-ui-prototype) which generates the `bootstrap.css` file we are using here.

The general approach should be to not modify the `bootstrap.css`, so that we can keep it easily updatable.
The initial modifications of this file were made in order to allow for this themeing approach.
Because bootstrap has colors spread all over the place we'd otherwise have to override mostly everything, that's why these general modifications are in the main `bootstrap.css` file.
