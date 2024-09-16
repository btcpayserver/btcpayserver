//https://github.com/ant7/chartist-plugin-tooltip2
(function (root, factory) {
    if (typeof define === 'function' && define.amd) {
        // AMD. Register as an anonymous module.
        define(["chartist"], function (Chartist) {
            return (root.returnExportsGlobal = factory(Chartist));
        });
    } else if (typeof module === 'object' && module.exports) {
        // Node. Does not work with strict CommonJS, but
        // only CommonJS-like environments that support module.exports,
        // like Node.
        module.exports = factory(require("chartist"));
    } else {
        // Browser globals (root is window)
        root['Chartist.plugins.tooltip2'] = factory(Chartist);
    }
}(typeof self !== 'undefined' ? self : this, function (Chartist) {

    /**
     * Chartist.js plugin to display a tooltip on top of a chart.
     * @author  Antonia Ciocodeica
     * @version 0.3 25 Nov 2016
     */
    (function(window, document, Chartist) {
        'use strict';


        var startId = 0;

        var publicOptions = {
            cssClass: 'chartist-tooltip',
            offset: {
                x: 0,
                y: -20,
            },
            offsetCollision: {
                x: 20,
                y: 0, // vertical collision not implemented
            },

            // Value transform function
            // It receives a single argument that contains the current value
            // "this" is the current chart
            // It must return the formatted value to be added in the tooltip (eg: currency format)
            valueTransformFunction: null,

            // Use an already existing element as a template for the tooltip.
            // The content of the element must be a Mustache-style template
            // {{value}} {{metaElement}}
            elementTemplateSelector: null,

            // Markup to use as a template for the content of the tooltip
            template: '<p>{{meta}}: {{value}}</p>',

            hideDelay: 500,

            // If you choose to reverse the original order of the chart elements in
            // the DOM, you must set this to true
            dataDrawnReversed: false,

            // only if a custom element is used for the trigger (TODO: test)
            triggerSelector: null,

            id: null,
        };

        Chartist.plugins = Chartist.plugins || {};

        Chartist.plugins.tooltip2 = function(options) {
            options = Chartist.extend({}, publicOptions, options);

            /**
             * Chartist tooltip plugin
             * @param Chart chart
             */
            return function tooltip(chart) {
                startId ++;

                // simple unique id for the tooltip element (needed to be able to
                // add aria-describedby to the trigger while the tooltip is visible)
                options.id = 'charttooltip-' + startId;
                var triggerSelector = getTriggerSelector();
                var hoverClass = getDefaultTriggerClass() + '--hover';
                var tooltipElement = getTooltipElement();
                var pointValues = getPointValues();
                var hideDelayTimer;
                var containerRect;

                options.template = tooltipElement.innerHTML;

                init();

                /**
                 * Initialize the tooltip
                 */
                function init() {
                    if (!chart.container) {
                        return;
                    }

                    // set attribute on the container, so external scripts can detect the tooltip element
                    chart.container.setAttribute('data-charttooltip-id', options.id);

                    // set the initial position for the tooltip (top / left corner of the chart container)
                    setTooltipPosition(chart.container, true);

                    // Offer support for multiple series line charts
                    if (chart instanceof Chartist.Line) {
                        chart.on('created', function() {
                            if (pointValues.length === 0) {
                                return;
                            }

                            chart.container.querySelector('svg').addEventListener('mousemove', prepareLineTooltip);
                            chart.container.addEventListener('mouseleave', function(e) {
                                var pointElement = chart.container.querySelector('.' + chart.options.classNames.point + '--hover');
                                hideTooltip(pointElement);
                            });
                        });

                        return;
                    }

                    chart.container.addEventListener('mouseover', delegate(triggerSelector, function(e) {
                        showTooltip(e.target);
                    }));
                    chart.container.addEventListener('mouseout', delegate(triggerSelector, function(e) {
                        hideTooltip(e.target);
                    }));
                }


                /**
                 * Prepare line tooltip
                 * Calculates the closest point on the line according to the current position of the mouse
                 * @param Event e
                 */
                function prepareLineTooltip(e) {
                    var boxData = this.getBoundingClientRect();
                    var currentXPosition = e.pageX - (boxData.left + (document.documentElement.scrollLeft || document.body.scrollLeft));
                    var currentYPosition = e.pageY - (boxData.top + (document.documentElement.scrollTop || document.body.scrollTop));
                    var closestPointOnX = getClosestNumberFromArray(currentXPosition, pointValues);

                    var pointElements = chart.container.querySelectorAll('.' + chart.options.classNames.point + '[x1="' + closestPointOnX + '"]');
                    var pointElement;

                    if (pointElements.length <= 1) {
                        pointElement = pointElements[0];
                    } else {
                        var yPositions = [];
                        var closestPointOnY;

                        Array.prototype.forEach.call(pointElements, function(point) {
                            yPositions.push(point.getAttribute('y1'));
                        });

                        closestPointOnY = getClosestNumberFromArray(currentYPosition, yPositions);
                        pointElement = chart.container.querySelector('.' + chart.options.classNames.point + '[x1="' + closestPointOnX + '"][y1="' + closestPointOnY + '"]');
                    }

                    if (!pointElement || matches(pointElement, '.' + hoverClass)) {
                        return;
                    }

                    showTooltip(pointElement);
                }

                /**
                 * Show tooltip
                 * @param Element triggerElement
                 */
                function showTooltip(triggerElement) {
                    var meta;
                    var value;
                    var textMarkup = options.template;
                    var seriesName;
                    var seriesGroups;
                    var seriesIndex;
                    var valueGroup;
                    var valueIndex;
                    var itemData;
                    var seriesData;

                    clearTimeout(hideDelayTimer);

                    if (!triggerElement) {
                        return;
                    }

                    seriesName = triggerElement.parentNode.getAttribute('ct:series-name');
                    seriesGroups = Array.prototype.slice.call(triggerElement.parentNode.parentNode.children);
                    seriesIndex = options.dataDrawnReversed ? seriesGroups.reverse().indexOf(triggerElement.parentNode) : seriesGroups.indexOf(triggerElement.parentNode);

                    valueGroup = Array.prototype.slice.call(triggerElement.parentNode.querySelectorAll('.' + getDefaultTriggerClass()));
                    valueIndex = valueGroup.indexOf(triggerElement);

                    // clone the series array
                    seriesData = chart.data.series.slice(0);
                    seriesData = chart.options.reverseData ? seriesData.reverse()[seriesIndex] : seriesData[seriesIndex];
                    seriesData = (!Array.isArray(seriesData) && typeof seriesData == 'object' && seriesData.data) ? seriesData.data : seriesData;

                    if (!seriesData) return;
                    itemData = (!Array.isArray(seriesData) && typeof seriesData == 'object') ? seriesData : seriesData[valueIndex];
                    if (!itemData && typeof seriesData == 'number') itemData = { value: seriesData, meta: chart.data.labels[seriesIndex] };
                    if (typeof itemData == 'undefined') return;
                    meta = itemData.meta;
                    value = itemData.value || itemData;
                    if (typeof itemData == 'undefined') return;
                    if (typeof options.valueTransformFunction === 'function') {
                        value = options.valueTransformFunction.call(chart, value, chart.data.labels[valueIndex], valueIndex);
                    }

                    // Remove the hover class and the aria-describedby attribute from the currently active triggers
                    var activeTriggerElements = chart.container.querySelectorAll('.' + hoverClass);
                    Array.prototype.forEach.call(activeTriggerElements, function(activeTriggerElement) {
                        activeTriggerElement.classList.remove(hoverClass);
                        activeTriggerElement.removeAttribute('aria-describedby');
                    });

                    // add hover class to the current active trigger
                    triggerElement.classList.add(hoverClass);
                    triggerElement.setAttribute('aria-describedby', options.id);

                    // value
                    textMarkup = textMarkup.replace(new RegExp('{{value}}', 'gi'), value);

                    // replace all known {{}} occurences with their respective values
                    if (meta && typeof meta === 'object') {
                        for (var metaKey in meta) {
                            textMarkup = textMarkup.replace(new RegExp('{{' + metaKey + '}}', 'gi'), meta[metaKey] || '');
                        }
                    } else {
                        textMarkup = textMarkup.replace(new RegExp('{{meta}}', 'gi'), meta || '');
                    }

                    // series name
                    textMarkup = textMarkup.replace(new RegExp('{{seriesName}}', 'gi'), seriesName || '');
                    tooltipElement.innerHTML = textMarkup;
                    tooltipElement.removeAttribute('hidden');
                    setTooltipPosition(triggerElement);
                }

                /**
                 * Hide tooltip
                 * @param Elemet triggerElement
                 */
                function hideTooltip(triggerElement) {
                    if (!triggerElement) {
                        return;
                    }

                    hideDelayTimer = setTimeout(function() {
                        triggerElement.removeAttribute('aria-describedby');
                        tooltipElement.setAttribute('hidden', true);
                        triggerElement.classList.remove(getDefaultTriggerClass() + '--hover');
                    }, options.hideDelay);
                }

                /**
                 * Get tooltip element
                 * @return Element
                 */
                function getTooltipElement() {
                    var tooltipElement = document.getElementById(options.id);

                    if (tooltipElement) {
                        return tooltipElement;
                    }

                    return createTooltipElement();

                }

                /**
                 * Create tooltip element
                 * @return Element
                 */
                function createTooltipElement() {
                    var tooltipElement = document.createElement('div');
                    var tooltipTemplateElement;

                    if (options.elementTemplateSelector) {
                        tooltipTemplateElement = document.querySelector(options.elementTemplateSelector);
                        if (tooltipTemplateElement) {
                            if (tooltipTemplateElement.nodeName == 'TEMPLATE') {
                                tooltipElement.innerHTML = tooltipTemplateElement.innerHTML;
                            } else {
                                tooltipElement = tooltipTemplateElement.cloneNode(true);
                            }
                        }
                    }

                    if (!tooltipTemplateElement) {
                        tooltipElement.innerHTML = options.template;
                    }

                    tooltipElement.classList.add(options.cssClass);
                    tooltipElement.id = options.id;

                    tooltipElement.setAttribute('role', 'tooltip');
                    tooltipElement.setAttribute('hidden', 'true');

                    document.body.appendChild(tooltipElement);

                    return tooltipElement;
                }

                /**
                 * Set tooltip position
                 * @param Element relativeElement
                 * @param Boolean ignoreClasses
                 */
                function setTooltipPosition(relativeElement, ignoreClasses) {
                    containerRect = chart.container.getBoundingClientRect();
                    var isLine = tooltipElement.innerHTML.match('chartist-tooltip-line');
                    var positionData = getTooltipPosition(relativeElement, isLine);

                    tooltipElement.style.transform = 'translate(' + positionData.left + 'px, ' + positionData.top + 'px)';
                    if (isLine)
                        tooltipElement.style.height = containerRect.height + options.offset.y + 'px';

                    if (ignoreClasses) {
                        return;
                    }

                    tooltipElement.classList.remove(options.cssClass + '--right');
                    tooltipElement.classList.remove(options.cssClass + '--left');
                    tooltipElement.classList.add(options.cssClass + '--' + positionData.alignment);
                }

                /**
                 * Get tooltip position relative to an element
                 * @param Element relativeElement
                 * @return Object positionData
                 */
                function getTooltipPosition(relativeElement, isLine) {
                    var positionData = {
                        alignment: 'center',
                    };
                    var width = tooltipElement.offsetWidth;
                    var height = tooltipElement.offsetHeight;

                    var boxData = relativeElement.getBoundingClientRect();
                    var left = boxData.left + window.scrollX + options.offset.x - width / 2 + boxData.width / 2;
                    var top = isLine
                        ? containerRect.top + window.scrollY + options.offset.y
                        : boxData.top + window.scrollY - height + options.offset.y;

                    // Minimum horizontal collision detection
                    if (left + width > document.body.clientWidth) {
                        left = left - width / 2 + options.offsetCollision.x;
                        positionData.alignment = 'right';
                    } else if (left < 0) {
                        left = boxData.left + window.scrollX - options.offsetCollision.x;
                        positionData.alignment = 'left';
                    }

                    positionData.left = left;
                    positionData.top = top;

                    return positionData;
                }

                /**
                 * Get trigger selector
                 * @return String The selector of the element that should trigger the tooltip
                 */
                function getTriggerSelector() {
                    if (options.triggerSelector) {
                        return options.triggerSelector;
                    }

                    return '.' + getDefaultTriggerClass();
                }

                /**
                 * Get default trigger class from the chart instance
                 * @return string chart.options.classNames.[specificClassName]
                 */
                function getDefaultTriggerClass() {
                    if (chart instanceof Chartist.Bar) {
                        return chart.options.classNames.bar;
                    }
                    if (chart instanceof Chartist.Pie) {
                        return (chart.options.donut ? chart.options.classNames.sliceDonut : chart.options.classNames.slicePie);
                    }

                    return chart.options.classNames.point;
                }

                /**
                 * Get horizontal point values (only useful for the line type chart)
                 * @return Array pointValues The point values
                 */
                function getPointValues() {
                    var pointValues = [];

                    if (!(chart instanceof Chartist.Line)) {
                        return;
                    }

                    chart.on('draw', function(data) {
                        if (data.type == 'point') {
                            pointValues.push(data.x);
                        }
                    });

                    return pointValues;
                }

            }
        };

        /**
         * Delegate event
         * @param string selector
         * @param function listener
         * @returns function
         */
        function delegate(selector, listener) {
            return function(e) {
                var element = e.target;
                do {
                    if (!matches(element, selector)) {
                        continue;
                    }
                    e.delegateTarget = element;
                    listener.apply(this, arguments);
                    return;
                } while ((element = element.parentNode));
            };
        }

        /**
         * Matches selector
         * @param Element el
         * @param string selector
         * @returns bool
         */
        function matches(el, selector) {
            var matchesFunction = el.matches || el.webkitMatchesSelector || el.mozMatchesSelector || el.msMatchesSelector;
            if (matchesFunction) {
                return matchesFunction.call(el, selector);
            }
        }

        /**
         * Get the closest number from an array
         * @param Int/Float number
         * @param Array array
         * @return Int The value from the array that is closest to the number
         */
        function getClosestNumberFromArray(number, array) {
            return array.reduce(function (previous, current) {
                return (Math.abs(current - number) < Math.abs(previous - number) ? current : previous);
            });
        }

    }(window, document, Chartist));

    // Just return a value to define the module export.
    return Chartist.plugins.tooltip2;
}));
