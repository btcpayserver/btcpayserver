function Cart() {
    this.items = 0;
    this.totalAmount = 0;
    this.content = [];

    this.loadLocalStorage();
    this.buildUI();

    this.$items = $('#js-cart-items');
    this.$total = $('.js-cart-total');
    this.$summaryProducts = $('.js-cart-summary-products');
    this.$summaryDiscount = $('.js-cart-summary-discount');
    this.$summaryTotal = $('.js-cart-summary-total');
    this.$summaryTip = $('.js-cart-summary-tip');
    this.listItems();

    this.updateItemsCount();
    this.updateAmount();
    this.updatePosData();
}

// Get total amount of products
Cart.prototype.getTotalProducts = function() {
    var amount = 0 ;

    // Always calculate the total amount based on the cart content
    for (var key in this.content) {
        if (
            this.content.hasOwnProperty(key) &&
            typeof this.content[key] != 'undefined' &&
            !this.content[key].disabled
        ) {
            const price = this.toCents(this.content[key].price ||0);
            amount += (this.content[key].count * price);
        }
    }

    return amount;
}

// Get absolute total amount
Cart.prototype.getTotal = function(includeTip) {
    this.totalAmount = this.getTotalProducts();

    if (this.getDiscount() > 0) {
        this.totalAmount -= this.getDiscountAmount(this.totalAmount);
    }

    if (includeTip) {
        this.totalAmount += this.getTip();
    }

    return this.fromCents(this.totalAmount);
}

Cart.prototype.decrementItem = function(id) {
    var self = this;
    this.items = 0; // Calculate total # of items from scratch just to make sure

    this.content.filter(function(obj, index, arr){
        // Decrement the item count
        if (obj.id === id)
        {
            obj.count--;
            delete(obj.disabled);

            // It's the last item with the same ID, remove it
            if (obj.count <= 0) {
                self.removeItem(id, index, arr);
            }
        }

        self.items += obj.count;
    });

    this.updateAll();
}

Cart.prototype.removeItemAll = function(id) {
    var self = this;
    this.items = 0;

    // Remove by item
    if (typeof id != 'undefined') {
        this.content.filter(function(obj, index, arr){
            if (obj.id === id) {
                self.removeItem(id, index, arr);

                for (var i = 0; i < obj.count; i++) {
                    self.items--;
                }
            }

            self.items += obj.count;
        });
    } else { // Remove all
        this.$list.find('tbody').empty();
        this.content = [];
    }

    this.updateAll();
}

Cart.prototype.removeItem = function(id, index, arr) {
    // Remove from the array
    arr.splice(index, 1);
    // Remove from the DOM
    this.$list.find('tr').eq(index+1).remove();
}

/*
* Update DOM
*/
// Update all data elements
Cart.prototype.updateAll = function() {
    this.saveLocalStorage();
    this.updateItemsCount();
    this.updateDiscount();
    this.updateSummaryProducts();
    this.updateSummaryTotal();
    this.updateTotal();
    this.updateAmount();
    this.updatePosData();
}

// Update number of cart items
Cart.prototype.updateItemsCount = function() {
    this.$items.text(this.items);
}

// Update total products (including the custom amount and discount) in the cart
Cart.prototype.updateTotal = function() {
    this.$total.text(this.formatCurrency(this.getTotal()));
}

// Update total amount in the summary
Cart.prototype.updateSummaryTotal = function() {
    this.$summaryTotal.text(this.formatCurrency(this.getTotal(true)));
}

// Update total products amount in the summary
Cart.prototype.updateSummaryProducts = function() {
    this.$summaryProducts.text(this.formatCurrency(this.fromCents(this.getTotalProducts())));
}

// Update discount amount in the summary
Cart.prototype.updateDiscount = function(amount) {
    var discount = 0;

    if (typeof amount != 'undefined') {
        discount = amount;
    } else {
        discount = this.percentage(this.getTotalProducts(), this.getDiscount());
        discount = this.fromCents(discount);
    }

    this.$summaryDiscount.text((discount > 0 ? '-' : '') + this.formatCurrency(discount));
}

// Update tip amount in the summary
Cart.prototype.updateTip = function(amount) {
    var tip = typeof amount != 'undefined' ? amount : this.fromCents(this.getTip());

    this.$summaryTip.text(this.formatCurrency(tip));
}

// Update hidden total amount value to be sent to the checkout page
Cart.prototype.updateAmount = function() {
    $('#js-cart-amount').val(this.getTotal(true));
    $('#js-cart-tip').val(this.tip);
    $('#js-cart-discount').val(this.discount);
}
Cart.prototype.updatePosData = function() {
    var result = {
      cart: this.content,
      discountPercentage: this.discount? parseFloat(this.discount): 0,
      subTotal: this.fromCents(this.getTotalProducts()),
      discountAmount: this.fromCents(this.getDiscountAmount(this.totalAmount)),
      tip: this.tip? this.tip: 0,
      total: this.getTotal(true)
    };
    $('#js-cart-posdata').val(JSON.stringify(result));
}

Cart.prototype.resetDiscount = function() {
    this.setDiscount(0);
    this.updateDiscount(0);
    $('.js-cart-discount').val('');
}

Cart.prototype.resetTip = function() {
    this.setTip(0);
    this.updateTip(0);
    $('.js-cart-tip').val('');
}

// Escape html characters
Cart.prototype.escape = function(input) {
    return ('' + input) /* Forces the conversion to string. */
        .replace(/&/g, '&amp;') /* This MUST be the 1st replacement. */
        .replace(/'/g, '&apos;') /* The 4 other predefined entities, required. */
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
    ;
}

// Load the template
Cart.prototype.template = function($template, obj) {
    var template = $template.text();

    for (var key in obj) {
        var re = new RegExp('{' + key + '}', 'mg');
        template = template.replace(re, obj[key]);
    }

    return template;
}

// Build the cart skeleton
Cart.prototype.buildUI = function() {
    var $table = $('#js-cart-extra').find('thead'),
        list = [];

    tableTemplate = this.template($('#template-cart-extra'), {
        'discount': this.escape(this.fromCents(this.getDiscount()) || ''),
    });
    list.push($(tableTemplate));

    tableTemplate = this.template($('#template-cart-total'), {
        'total': this.escape(this.formatCurrency(this.getTotal()))
    });
    list.push($(tableTemplate));

    // Add the list to DOM
    $table.append(list);

    // Change total when discount is changed
    $('.js-cart-discount').inputAmount(this, 'discount');
    // Remove discount
    $('.js-cart-discount-remove').removeAmount(this, 'discount');
}

// List cart items and bind their events
Cart.prototype.listItems = function() {
    var $table = this.$list.find('tbody'),
        self = this,
        list = [],
        tableTemplate = '';
    if (this.content.length > 0) {
        // Prepare the list of items in the cart
        for (var key in this.content) {
            var item = this.content[key],
                image = item.image && this.escape(item.image);
            
            if (image && image.startsWith("~")) {
                image = image.replace('~', window.location.pathname.substring(0, image.indexOf('/apps')));
            }

            tableTemplate = this.template($('#template-cart-item'), {
                'id': this.escape(item.id),
                'image': image ? this.template($('#template-cart-item-image'), {
                    'image' : image
                }) : '',
                'title': this.escape(item.title),
                'count': this.escape(item.count),
                'inventory': this.escape(item.inventory < 0? 99999: item.inventory),
                'price': this.escape(item.price || 0)
            });
            list.push($(tableTemplate));
        }
    }
}

/*
* Currencies and numbers
*/
Cart.prototype.formatCurrency = function(amount) {
    var amt = '',
        thousandsSep = '',
        decimalSep = ''
        prefix = '',
        postfix = '';

    if (srvModel.currencyInfo.prefixed) {
        prefix = srvModel.currencyInfo.currencySymbol;
        if (srvModel.currencyInfo.symbolSpace) {
            prefix = prefix + ' ';
        }

    }
    else {
        postfix = srvModel.currencyInfo.currencySymbol;
        if (srvModel.currencyInfo.symbolSpace) {
            postfix = ' ' + postfix;
        }

    }
    thousandsSep = srvModel.currencyInfo.thousandSeparator;
    decimalSep = srvModel.currencyInfo.decimalSeparator;
    amt = amount.toFixed(srvModel.currencyInfo.divisibility);

    // Add currency sign and thousands separator
    var splittedAmount = amt.split('.');
    amt = (splittedAmount[0] + '.').replace(/(\d)(?=(\d{3})+\.)/g, '$1' + thousandsSep);
    amt = amt.substr(0, amt.length - 1);
    if(splittedAmount.length == 2) {
        amt = amt + decimalSep + splittedAmount[1];
    }
    if (srvModel.currencyInfo.divisibility !== 0) {
        amt[amt.length - srvModel.currencyInfo.divisibility - 1] = decimalSep;
    }
    amt = prefix + amt + postfix;

    return amt;
}

Cart.prototype.toNumber = function(num) {
    return (num * 1) || 0;
}

Cart.prototype.toCents = function(num) {
    return num * Math.pow(10, srvModel.currencyInfo.divisibility);
}

Cart.prototype.fromCents = function(num) {
    return num / Math.pow(10, srvModel.currencyInfo.divisibility);
}

Cart.prototype.percentage = function(amount, percentage) {
    return this.fromCents((amount / 100) * percentage);
}

Cart.prototype.destroy = function(keepAmount) {
    this.resetDiscount();
    this.resetTip();

    // When form is sent
    if (keepAmount) {
        this.content = [];
        this.items = 0;
    } else {
        this.removeItemAll();
    }
    localStorage.removeItem(this.getStorageKey('cart'));
}


/*
* jQuery helpers
*/
$.fn.inputAmount = function(obj, type) {
    $(this).off().on('input', function(event){
        var val = obj.toNumber($(this).val());

        switch (type) {
            case 'discount':
                obj.setDiscount(val);
                obj.updateDiscount();
                obj.updateSummaryProducts();
                obj.updateTotal();
                obj.resetTip();
                break;
            case 'tip':
                obj.setTip(val);
                obj.updateTip();
                break;
        }

        obj.updateSummaryTotal();
        obj.updateAmount();
        obj.updatePosData();
    });
}

$.fn.removeAmount = function(obj, type) {
    $(this).off().on('click', function(event){
        event.preventDefault();

        switch (type) {
            case 'discount':
                obj.resetDiscount();
                obj.updateSummaryProducts();
                break;
        }

        obj.resetTip();
        obj.updateTotal();
        obj.updateSummaryTotal();
    });
}
