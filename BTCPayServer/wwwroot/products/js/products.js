function Products() {
    this.products = [];

    // Get products from template
    this.loadFromTemplate();

    // Show products in the DOM
    this.showAll();
}

Products.prototype.loadFromTemplate = function() {
    var template = $('.js-product-template').val().trim();
    
    var lines = [];
    var items = template.split("\n");
    for (var i = 0; i < items.length; i++) {
        if(items[i] === ""){
            continue;
        }
        if(items[i].startsWith("  ")){
            lines[lines.length-1]+=items[i] + "\n";
        }else{
           
            lines.push(items[i] + "\n");
        }
    }

    this.products = [];

    // Split products from the template
    for (var kl in lines) {
        var line = lines[kl],
            product = line.split("\n"),
            id, price, title, description, image = null,
            custom, inventory=null;

        for (var kp in product) {
            var productProperty = product[kp].trim();

            if (kp == 0) {
                id = productProperty;
            }

            if (productProperty.indexOf('price:') !== -1) {
                price = parseFloat(productProperty.replace('price:', '').trim()).noExponents();
            }
            if (productProperty.indexOf('title:') !== -1) {
                title = productProperty.replace('title:', '').trim();
            }
            if (productProperty.indexOf('description:') !== -1) {
                description = productProperty.replace('description:', '').trim();
            }
            if (productProperty.indexOf('image:') !== -1) {
                image = productProperty.replace('image:', '').trim();
            }
            if (productProperty.indexOf('custom:') !== -1) {
                custom = productProperty.replace('custom:', '').trim();
            }
            if (productProperty.indexOf('inventory:') !== -1) {
                inventory = parseInt(productProperty.replace('inventory:', '').trim(),10);
            }
        }

        if (price != null || title != null) {
            // Add product to the list
            this.products.push({
                'id': id,
                'title': title,
                'price': price,
                'image': image || null,
                'description': description || '',
                'custom': Boolean(custom),
                'inventory': isNaN(inventory)? null: inventory
            });
        }
        
    }
};

Products.prototype.saveTemplate = function() {
    var template = '';

    // Construct template from the product list
    for (var key in this.products) {
        var product = this.products[key],
            id = product.id,
            title = product.title,
            price = product.price? product.price : 0,
            image = product.image,
            description = product.description,
            custom = product.custom,
            inventory = product.inventory;

        template += id + '\n' +
        '  price: ' + parseFloat(price).noExponents() + '\n' +
        '  title: ' + title + '\n';

        if (description) {
            template += '  description: ' + description + '\n';
        }
        if (image) {
            template += '  image: ' + image + '\n';
        }
        if (custom) {
            template += '  custom: true\n';
        }
        if(inventory != null){
            template+= '  inventory: ' + inventory + '\n';
        }
        template += '\n';
    }

    $('.js-product-template').val(template);
};

Products.prototype.showAll = function() {
    var list = [];

    for (var key in this.products) {
        var product = this.products[key],
            image = product.image;

        list.push(this.template($('#template-product-item'), {
            'title': this.escape(product.title),
            'image': image ? '<img class="card-img-top" src="' + this.escape(image) + '" alt="Card image cap">' : ''
        }));
    }

    $('.js-products').html(list);
};

// Load the template
Products.prototype.template = function($template, obj) {
    var template = $template.text();

    for (var key in obj) {
        var re = new RegExp('{' + key + '}', 'mg');
        template = template.replace(re, obj[key]);
    }

    return template;
};

Products.prototype.saveItem = function(obj, index) {
    // Edit product
    if (index) {
        this.products[index] = obj;
    } else { // Add new product
        this.products.push(obj);
    }

    this.saveTemplate();
    this.showAll();
    this.modalEmpty();
}

Products.prototype.removeItem = function(index) {
    if (this.products.length == 1) {
        this.products = [];
        $('.js-products').html('No products.');
    } else {
        this.products.splice(index, 1);
        $('.js-products').find('.card').parent().eq(index).remove();
    }

    this.saveTemplate();
};

Products.prototype.itemContent = function(index) {
    var product = null,
        custom = false;

    // Existing product
    if (!isNaN(index)) {
        product = this.products[index];
        custom = product.custom;
    }

    var template = this.template($('#template-product-content'), {
        'id': product != null ? this.escape(product.id) : '',
        'index': isNaN(index) ? '' : this.escape(index),
        'price': product != null ? parseFloat(this.escape(product.price)).noExponents() : '',
        'title': product != null ? this.escape(product.title) : '',
        'description': product != null ? this.escape(product.description) : '',
        'image': product != null ? this.escape(product.image) : '',
        'inventory': product != null ? parseInt(this.escape(product.inventory),10) : '',
        'custom': '<option value="true"' + (custom ? ' selected' : '') + '>Yes</option><option value="false"' + (!custom ? ' selected' : '') + '>No</option>'
    });

    $('#product-modal').find('.modal-body').html(template);
};

Products.prototype.modalEmpty = function() {
    var $modal = $('#product-modal');

    $modal.modal('hide');
    $modal.find('.modal-body').empty();
}

Products.prototype.escape = function(input) {
    return ('' + input) /* Forces the conversion to string. */
        .replace(/&/g, '&amp;') /* This MUST be the 1st replacement. */
        .replace(/'/g, '&apos;') /* The 4 other predefined entities, required. */
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
    ;
}

Number.prototype.noExponents= function(){
    var data= String(this).split(/[eE]/);
    if(data.length== 1) return data[0];

    var  z= '', sign= this<0? '-':'',
        str= data[0].replace('.', ''),
        mag= Number(data[1])+ 1;

    if(mag<0){
        z= sign + '0.';
        while(mag++) z += '0';
        return z + str.replace(/^\-/,'');
    }
    mag -= str.length;
    while(mag--) z += '0';
    return str + z;
};
