
# GreenField API Development Documentation
## Adding new API endpoints

* Always document all endpoints and model schemas in swagger. OpenAPI 3.0 is used as a specification, in JSON formatting, and is written manually. The specification is split to a file per controller and then merged by the server through a controller action at  `/swagger/v1/swagger.json`.
* All `JsonConverter` usage should be registered through attributes within the model itself.
* `decimal` and `long` and other similar types, if there is a need for decimal precision or has the possibility of an overflow issue, should be serialized to a string and able to deserialize from the original type and a string.
* Ensure that the correct security permissions are set on the endpoint. Create a new permission if none of the existing ones are suitable.
* Use HTTP methods according to REST principles when possible. This means:
  * `POST` - Create or custom action
  * `PUT` - Update full model
  * `PATCH` - Update partially
  * `DELETE` - Delete or Archive
* When returning an error response, we should differentiate from 2 possible scenarios:
  * Model validation - an error or errors on the request was found - [Status Code 422](https://httpstatuses.com/422) with the model:
	```json
	[
	  {
	    "path": "prop-name",
	    "message": "human readable message"
	  }
	]
	```
  * Generic request error - an error resulting from the business logic unable to handle the specified request - [Status Code 400](https://httpstatuses.com/400) with the model:
	```json
	{
	  "code": "unique-error-code",
	  "message":"a human readable message"
	}
	```

## Updating existing API endpoints

### Scenario 1: Changing a property type on the model
Changing a property on a model is a breaking change unless the server starts handling both versions.

#### Solutions
* Bump the version of the endpoint.

#### Alternatives considered
* Create a `JsonConverter` that allows conversion between the original type and the new type. However, if this option is used, you will need to ensure that the response model returns the same format. In the case of the `GET` endpoint, you will break clients expecting the original type.

### Scenario 2: Removing a property on the model
Removing a property on a model is a breaking change. 

#### Solutions
* Bump the version of the endpoint.

#### Alternatives considered
* Create a default value (one that is not useful) to be sent back in the model.  Ignore the property being sent on the model to the server.

### Scenario 3: Adding a property on the model
Adding a property on a model can potentially be a breaking change. It is a breaking change if:
* the property is required.
* the property has no default value.

#### Solutions
*  Check if the payload has the property present. If not, either set to the default value (in the case of a`POST`) or set to the model's current value. See [Detecting missing properties in a JSON model](#missing-properties-detect) for how to achieve this.

#### Alternatives considered
* Bump the version of the endpoint.
* Assume the property is always sent and let the value be set to the default if not ( in the case of nullable types, this may be problematic when calling update endpoints). 
* Use [`[JsonExtensionData]AdditionalData`](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_JsonExtensionDataAttribute.htm) so that clients receive the full payload even after updating only the server. This is problematic as it only fixes clients which implement this opinionated flow (this is not a standard or common way of doing API calls) .



## Technical specifics

### <a name="missing-properties-detect"></a>Detecting missing properties in a JSON model.
Possible solutions:
* Read the raw JSON object in the controller action and search for the lack of a specific property.
* Use [`JSON.NET Serialization Callabacks`](https://www.newtonsoft.com/json/help/html/SerializationCallbacks.htm) to set a `List<string> MissingProperties;` variable.
