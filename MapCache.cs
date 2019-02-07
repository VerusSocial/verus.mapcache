using AutoMapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GPS.Domain.Utilities
{
    public static class MapCache
    {
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, IMapper>> MapperDictionary = new ConcurrentDictionary<Type, ConcurrentDictionary<Type, IMapper>>();

        public static MapperConfiguration Configuration<I, O>(I input, O output)
        {
            return Configuration(typeof(I), typeof(O));
        }

        public static MapperConfiguration Configuration(Type inputType, Type outputType)
        {
            return new MapperConfiguration((expression) => {
                var map = expression.CreateMap(inputType, outputType);

                var inputProperties = PropertyDictionary(inputType);
                var outputProperties = PropertyDictionary(outputType);

                foreach (var kvp in outputProperties)
                {
                    if (!inputProperties.ContainsKey(kvp.Key))
                    {
                        map.ForMember(kvp.Key, mo => mo.Ignore());
                    }
                    else
                    {
                        var outputPropertyType = kvp.Value;
                        var inputPropertyType = inputProperties[kvp.Key];

                        if (outputPropertyType != inputPropertyType)
                        {
                            // if either is generic, then check the underlying primitive
                            // something like typeof(int?).IsGenericType -> true and typeof(bool?).IsGenericType -> true should be ignored
                            // something like typeof(decimal?).IsGenericType for inputPropertyType and outputPropertyType will be caught above
                            // this is looking for examples like int vs int?
                            if (inputPropertyType.IsGenericType == outputPropertyType.IsGenericType)
                            {
                                map.ForMember(kvp.Key, mo => mo.Ignore());
                            }
                            else if (!GenericUnderlyingTypeMatchesType(inputPropertyType, outputPropertyType) && !GenericUnderlyingTypeMatchesType(outputPropertyType, inputPropertyType))
                            {
                                map.ForMember(kvp.Key, mo => mo.Ignore());
                            }
                        }
                    }
                }
            });
        }

        private static bool GenericUnderlyingTypeMatchesType(Type genericType, Type type)
        {
            if (!genericType.IsGenericType)
                return false;

            var underlyingType = Nullable.GetUnderlyingType(genericType);

            if (underlyingType == type)
                return true;

            return false;
        }

        private static IMapper DefaultMapper<I, O>(I input, O output)
        {
            return DefaultMapper(typeof(I), typeof(O));
        }

        private static IMapper DefaultMapper(Type inputType, Type outputType)
        {
            var defaultMappings = MapCache.DefaultMappings(inputType);

            if (!defaultMappings.TryGetValue(outputType, out IMapper mapper))
            {
                var configuration = MapCache.Configuration(inputType, outputType);
                mapper = configuration.CreateMapper();

                mapper = defaultMappings.GetOrAdd(outputType, mapper);
            }

            return mapper;
        }

        private static ConcurrentDictionary<Type, IMapper> DefaultMappings<I>(I input)
        {
            return MapCache.DefaultMappings(typeof(I));
        }

        private static ConcurrentDictionary<Type, IMapper> DefaultMappings(Type inputType)
        {
            if (!MapperDictionary.TryGetValue(inputType, out ConcurrentDictionary<Type, IMapper> mapper))
            {
                mapper = MapperDictionary.GetOrAdd(inputType, new ConcurrentDictionary<Type, IMapper>());
            }

            return mapper;
        }

        public static O Map<I, O>(I input, O output)
        {
            var mapper = DefaultMapper(input, output);
            return Map(input, output, mapper);
        }

        public static O Map<I, O>(I input, Func<O> createOutput)
        {
            return Map(input, createOutput());
        }

        public static O Map<I, O>(I input, O output, MapperConfiguration configuration)
        {
            return Map(input, output, configuration.CreateMapper());
        }

        public static O Map<I, O>(I input, O output, IMapper mapper)
        {
            mapper.Map(input, output);
            return output;
        }

        public static IEnumerable<O> MapEnumerable<I, O>(IEnumerable<I> inputs, Func<O> createOutput)
        {
            var mapper = DefaultMapper(typeof(I), typeof(O));
            return MapEnumerable(inputs, createOutput, mapper);
        }

        public static IEnumerable<O> MapEnumerable<I, O>(IEnumerable<I> inputs, Func<O> createOutput, MapperConfiguration configuration)
        {
            var mapper = configuration.CreateMapper();
            return MapEnumerable(inputs, createOutput, mapper);
        }

        public static IEnumerable<O> MapEnumerable<I, O>(IEnumerable<I> inputs, Func<O> createOutput, IMapper mapper)
        {
            foreach (var input in inputs)
            {
                yield return Map(input, createOutput(), mapper);
            }

            yield break;
        }

        public static Dictionary<string, Type> PropertyDictionary(Type type)
        {
            var dictionary = new Dictionary<string, Type>();

            var bindingFlags = BindingFlags.FlattenHierarchy
                | BindingFlags.Public
                | BindingFlags.Instance;

            var properties = new List<PropertyInfo>();

            if (type.IsInterface)
            {
                var considered = new List<Type>();
                var queue = new Queue<Type>();

                considered.Add(type);
                queue.Enqueue(type);

                while (queue.Count > 0)
                {
                    var subType = queue.Dequeue();

                    foreach (var subInterface in subType.GetInterfaces())
                    {
                        if (considered.Contains(subInterface)) continue;

                        considered.Add(subInterface);
                        queue.Enqueue(subInterface);
                    }

                    var typeProperties = subType.GetProperties(bindingFlags);

                    var newPropertyInfos = typeProperties
                        .Where(x => !properties.Contains(x));

                    properties.InsertRange(0, newPropertyInfos);
                }
            }
            else
            {
                properties.AddRange(type.GetProperties(bindingFlags));
            }

            foreach (var propertyInfo in properties)
            {
                dictionary.Add(propertyInfo.Name, propertyInfo.PropertyType);
            }

            return dictionary;
        }
    }
}
