function Cart() {
    this.items = 0;
    this.totalAmount = 0;
    this.content = [];

    this.loadLocalStorage();
    this.buildUI();

    this.$list = $('#js-cart-list');
    this.$items = $('#js-cart-items');
    this.$total = $('.js-cart-total');
    this.$summaryProducts = $('.js-cart-summary-products');
    this.$summaryDiscount = $('.js-cart-summary-discount');
    this.$summaryTotal = $('.js-cart-summary-total');
    this.$summaryTip = $('.js-cart-summary-tip');
    this.$destroy = $('.js-cart-destroy');
    this.$confirm = $('#js-cart-confirm');

    this.listItems();
    this.bindEmptyCart();
    
    this.updateItemsCount();
    this.updateAmount();
}

Cart.prototype.setCustomAmount = function(amount) {
    this.customAmount = this.toNumber(amount);

    if (this.customAmount > 0) {
        localStorage.setItem(this.getStorageKey('cartCustomAmount'), this.customAmount);
    } else {
        localStorage.removeItem(this.getStorageKey('cartCustomAmount'));
    }
    return this.customAmount;
}

Cart.prototype.getCustomAmount = function() {
    return this.toCents(this.customAmount);
}

Cart.prototype.setTip = function(amount) {
    this.tip = this.toNumber(amount);

    if (this.tip > 0) {
        localStorage.setItem(this.getStorageKey('cartTip'), this.tip);
    } else {
        localStorage.removeItem(this.getStorageKey('cartTip'));
    }
    return this.tip;
}

Cart.prototype.getTip = function() {
    return this.toCents(this.tip);
}

Cart.prototype.setDiscount = function(amount) {
    this.discount = this.toNumber(amount);

    if (this.discount > 0) {
        localStorage.setItem(this.getStorageKey('cartDiscount'), this.discount);
    } else {
        localStorage.removeItem(this.getStorageKey('cartDiscount'));
    }
    return this.discount;
}

Cart.prototype.getDiscount = function() {
    return this.toCents(this.discount);
}

Cart.prototype.getDiscountAmount = function(amount) {
    return this.percentage(amount, this.getDiscount());
}

// Get total amount of products
Cart.prototype.getTotalProducts = function() {
    var amount = 0 ;

    // Always calculate the total amount based on the cart content
    for (var key in this.content) {
        if (this.content.hasOwnProperty(key) && typeof this.content[key] != 'undefined') {
            var price = this.toCents(this.content[key].price.value);
            amount += (this.content[key].count * price);
        }
    }

    // Add custom amount
    amount += this.getCustomAmount();

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

/*
* Data manipulation
*/
// Add item to the cart or update its count
Cart.prototype.addItem = function(item) {
    var id = item.id,
        result = this.content.filter(function(obj){
            return obj.id === id;
        });

    // Add new item because it doesn't exist yet
    if (!result.length) {
        this.content.push({id: id, title: item.title, price: item.price, count: 0, image: item.image});
        this.emptyCartToggle();
    }

    // Increment item count
    this.incrementItem(id);
}

Cart.prototype.incrementItem = function(id) {
    // Increment the existing item count
    this.content.filter(function(obj){
        if (obj.id === id){
            obj.count++;
        }
    });

    this.items++;
    this.updateAll();
}

Cart.prototype.decrementItem = function(id) {
    var self = this;

    // Decrement the existing item count
    this.content.filter(function(obj, index, arr){
        if (obj.id === id)
        {
            obj.count--;

            // It's the last item with the same ID, remove it
            if (obj.count === 0) {
                self.removeItem(id, index, arr);
            }
        }
    });

    this.items--;
    this.updateAll();
}

Cart.prototype.removeItemAll = function(id) {
    var self = this;

    // Remove by item
    if (id) {
        this.content.filter(function(obj, index, arr){
            if (obj.id === id)
            {
                self.removeItem(id, index, arr);
    
                for (var i = 0; i < obj.count; i++) {
                    self.items--;
                }
            }
        });
    } else { // Remove all
        this.$list.find('tbody').empty();
        self.content = [];
        self.items = 0;
    }

    this.emptyCartToggle();
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
    var $table = $('#js-cart-extra').find('tbody'),
        list = [];

    tableTemplate = this.template($('#template-cart-extra'), {
        'discount': this.escape(this.fromCents(this.getDiscount()) || ''),
        'customAmount': this.escape(this.fromCents(this.getCustomAmount()) || '')
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

    // Change total when discount is changed
    $('.js-cart-custom-amount').inputAmount(this, 'customAmount');
    // Remove discount
    $('.js-cart-custom-amount-remove').removeAmount(this, 'customAmount');
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
                image = this.escape(item.image);

            tableTemplate = this.template($('#template-cart-item'), {
                'id': this.escape(item.id),
                'image': image ? this.template($('#template-cart-item-image'), {
                    'image' : image
                }) : '',
                'title': this.escape(item.title),
                'count': this.escape(item.count),
                'price': this.escape(item.price.formatted)
            });
            list.push($(tableTemplate));
        }

        // Add the list to DOM
        $table.html(list);
        list = [];

        // Update the cart when number of items is changed
        $('.js-cart-item-count').off().on('input', function(event){
            var _this = this,
                id = $(this).closest('tr').data('id'),
                count = parseInt($(this).val()),
                prevCount = parseInt($(this).data('prev')),
                increased = count > prevCount;
            
            // User hasn't inputed any number so stop here
            if (isNaN(count)) {
                return false;
            }

            $(this).data('prev', count);

            var item = self.content.filter(function(obj){
                return obj.id === id
            });
            
            // Must be in the loop because user may change the count manually by more than 1
            for (var i = 0; i < Math.abs(count - prevCount); i++) {
                if (increased) {
                    self.addItem({
                        id: id,
                        title: item.title,
                        price: item.price,
                        image: typeof item.image != 'underfined' ? item.image : null
                    });
                } else {
                    self.decrementItem(id);
                }
            }
        });

        // Remove item from the cart
        $('.js-cart-item-remove').off().on('click', function(event){
            event.preventDefault();

            self.removeItemAll($(this).closest('tr').data('id'));
        });

        // Increment item
        $('.js-cart-item-plus').off().on('click', function(event){
            event.preventDefault();

            var $val = $(this).parents('.input-group').find('.js-cart-item-count');
            
            $val.val(parseInt($val.val()) + 1);
            self.incrementItem($(this).closest('tr').data('id'));
        });

        // Decrement item
        $('.js-cart-item-minus').off().on('click', function(event){
            event.preventDefault();

            var $val = $(this).parents('.input-group').find('.js-cart-item-count'),
                id = $(this).closest('tr').data('id'),
                val = parseInt($val.val());

            if (val === 1) {
                self.removeItemAll(id);
            } else {
                $val.val(val - 1);
                self.decrementItem(id);
            }
        });
    }
}

Cart.prototype.bindEmptyCart = function() {
    var self = this;

    this.emptyCartToggle();

    this.$destroy.click(function(event){
        event.preventDefault();

        self.destroy();
        self.emptyCartToggle();
    });
}

Cart.prototype.emptyCartToggle = function() {
    if (this.content.length > 0 || this.getCustomAmount()) {
        this.$destroy.show();
        this.$confirm.removeAttr('disabled');
    } else {
        this.$destroy.hide();
        this.$confirm.attr('disabled', 'disabled');
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

/*
* Storage
*/
Cart.prototype.getStorageKey = function (name) { 
    return (name + srvModel.appId + srvModel.currencyCode); 
}

Cart.prototype.saveLocalStorage = function() {
    localStorage.setItem(this.getStorageKey('cart'), JSON.stringify(this.content));
}

Cart.prototype.loadLocalStorage = function() {
    this.content = $.parseJSON(localStorage.getItem(this.getStorageKey('cart'))) || [];

    // Get number of cart items
    for (var key in this.content) {
        if (this.content.hasOwnProperty(key) && typeof this.content[key] != 'undefined' && this.content[key] != null) {
            this.items += this.content[key].count;
        }
    }

    this.discount = localStorage.getItem(this.getStorageKey('cartDiscount'));
    this.customAmount = localStorage.getItem(this.getStorageKey('cartCustomAmount'));
    this.tip = localStorage.getItem(this.getStorageKey('cartTip'));
}

Cart.prototype.destroy = function(keepAmount) {
    this.setTip(0);
    this.setDiscount(0);
    this.setCustomAmount(0);
    // When form is sent
    if (keepAmount) {
        this.content = [];
        this.items = 0;
    } else {
        this.updateDiscount(0);
        this.updateTip(0);

        this.removeItemAll();
        $('.js-cart-discount').val('');
        $('.js-cart-tip').val('');
        $('.js-cart-custom-amount').val('');
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
            case 'customAmount':
                obj.setCustomAmount(val);
                obj.updateDiscount();
                obj.updateSummaryProducts();
                obj.updateTotal();
                break;
            case 'discount':
                obj.setDiscount(val);
                obj.updateDiscount();
                obj.updateSummaryProducts();
                obj.updateTotal();
                break;
            case 'tip':
                obj.setTip(val);
                obj.updateTip();
                break;
        }

        obj.updateSummaryTotal();
        obj.updateAmount();
        obj.emptyCartToggle();
    });
}

$.fn.removeAmount = function(obj, type) {
    $(this).off().on('click', function(event){
        event.preventDefault();
    
        switch (type) {
            case 'customAmount':
                obj.setCustomAmount(0);
                obj.updateSummaryProducts();
                $('.js-cart-custom-amount').val('');
                break;
            case 'discount':
                obj.setDiscount(0);
                obj.updateDiscount(0);
                obj.updateSummaryProducts();
                $('.js-cart-discount').val('');
                break;
            case 'tip':
                obj.setTip(0);
                obj.updateTip(0);
                $('.js-cart-tip').val('');
                break;
        
            default:
                break;
        }

        obj.updateTotal();
        obj.updateSummaryTotal();
        obj.emptyCartToggle();  
    });
}