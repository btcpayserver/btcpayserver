$(document).ready(function(){
    var products = new Products(),
        delay = null;

    $('.js-product-template').on('input', function(){
        products.loadFromTemplate();

        clearTimeout(delay);

        // Delay rebuilding DOM for performance reasons
        delay = setTimeout(function(){
            products.showAll();
        }, 1000);
    });

    $('.js-products').on('click', '.js-product-remove', function(event){
        event.preventDefault();
        
        var id = $(this).closest('.card').parent().index();

        products.removeItem(id);
    });

    $('.js-products').on('click', '.js-product-edit', function(event){
        event.preventDefault();
        
        var id = $(this).closest('.card').parent().index();

        products.itemContent(id);
    });

    $('.js-product-save').click(function(event){
        event.preventDefault();

        var index = $('.js-product-index').val(),
            description = $('.js-product-description').val(),
            image = $('.js-product-image').val(),
            custom = $('.js-product-custom').val(),
            inventory = parseInt($('.js-product-inventory').val(), 10);
            obj = {
                id: products.escape($('.js-product-id').val()),
                price: products.escape($('.js-product-price').val()),
                title: products.escape($('.js-product-title').val()),
            };

        // Only continue if price and title is provided
        if (obj.price && obj.title) {
            if (description != null) {
                obj.description = products.escape(description);
            }
            if (image) {
                obj.image = products.escape(image);
            }
            if (custom == 'true') {
                obj.custom = products.escape(custom);
            }

            // Create an id from the title for a new product
            if (!Boolean(index)) {
                obj.id = products.escape(obj.title.toLowerCase() + ':');
            }
            if(inventory != null && !isNaN(inventory ))
            obj.inventory = inventory;
            
            products.saveItem(obj, index);
        }
    });

    $('.js-product-add').click(function(){
        products.itemContent();
    });
});
