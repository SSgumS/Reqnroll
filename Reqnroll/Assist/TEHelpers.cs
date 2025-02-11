using Reqnroll.Assist.Attributes;
using Reqnroll.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Reqnroll.Assist
{
    internal static class TEHelpers
    {
        private static readonly Regex invalidPropertyNameRegex = new Regex(InvalidPropertyNamePattern, RegexOptions.Compiled);
        private const string InvalidPropertyNamePattern = @"[^\p{Lu}\p{Ll}\p{Lt}\p{Lm}\p{Lo}\p{Nl}\p{Nd}_]";

        internal static T CreateTheInstanceWithTheDefaultConstructor<T>(Table table, InstanceCreationOptions creationOptions)
        {
            var instance = (T)Activator.CreateInstance(typeof(T));
            LoadInstanceWithKeyValuePairs(table, instance, creationOptions);
            return instance;
        }

        internal static T CreateTheInstanceWithTheValuesFromTheTable<T>(Table table, InstanceCreationOptions creationOptions)
        {
            var constructor = GetConstructorMatchingToColumnNames<T>(table);
            if (constructor == null)
                throw new MissingMethodException($"Unable to find a suitable constructor to create instance of {typeof(T).Name}");

            var membersThatNeedToBeSet = GetMembersThatNeedToBeSet(table, typeof(T));

            var constructorParameters = constructor.GetParameters();
            var parameterValues = new object[constructorParameters.Length];

            var members = new List<string>(constructorParameters.Length);
            for (var parameterIndex = 0; parameterIndex < constructorParameters.Length; parameterIndex++)
            {
                var parameter = constructorParameters[parameterIndex];
                var parameterName = parameter.Name;
                var member = (from m in membersThatNeedToBeSet
                              where string.Equals(m.MemberName, parameterName, StringComparison.OrdinalIgnoreCase)
                              select m).FirstOrDefault();
                if (member != null)
                {
                    members.Add(member.MemberName);
                    parameterValues[parameterIndex] = member.GetValue();
                }
                else if (parameter.HasDefaultValue)
                    parameterValues[parameterIndex] = parameter.DefaultValue;
            }

            VerifyAllColumn(table, creationOptions, members);
            return (T)constructor.Invoke(parameterValues);
        }

        internal static bool ThisTypeHasADefaultConstructor<T>()
        {
            return typeof(T).GetConstructors().Any(c => c.GetParameters().Length == 0);
        }

        internal static ConstructorInfo GetConstructorMatchingToColumnNames<T>(Table table)
        {
            var projectedPropertyNames = from property in typeof(T).GetProperties()
                                         from row in table.Rows
                                         where IsMemberMatchingToColumnName(property, row.Id())
                                         select property.Name;

            return (from constructor in typeof(T).GetConstructors()
                    where !projectedPropertyNames.Except(
                        from parameter in constructor.GetParameters()
                        select parameter.Name, StringComparer.OrdinalIgnoreCase).Any()
                    select constructor).FirstOrDefault();
        }

        internal static bool IsMemberMatchingToColumnName(MemberInfo member, string columnName)
        {
            return member.Name.MatchesThisColumnName(columnName)
                || IsMatchingAlias(member, columnName);
        }

        internal static bool MatchesThisColumnName(this string propertyName, string columnName)
        {
            var normalizedColumnName = NormalizePropertyNameToMatchAgainstAColumnName(RemoveAllCharactersThatAreNotValidInAPropertyName(columnName));
            var normalizedPropertyName = NormalizePropertyNameToMatchAgainstAColumnName(propertyName);

            return normalizedPropertyName.Equals(normalizedColumnName, StringComparison.OrdinalIgnoreCase);
        }

        internal static string RemoveAllCharactersThatAreNotValidInAPropertyName(string name)
        {
            //Unicode groups allowed: Lu, Ll, Lt, Lm, Lo, Nl or Nd see https://msdn.microsoft.com/en-us/library/aa664670%28v=vs.71%29.aspx
            return invalidPropertyNameRegex.Replace(name, string.Empty);
        }

        internal static string NormalizePropertyNameToMatchAgainstAColumnName(string name)
        {
            // we remove underscores, because they should be equivalent to spaces that were removed too from the column names
            // we also ignore accents
            return name.Replace("_", string.Empty).ToIdentifier();
        }

        internal static void LoadInstanceWithKeyValuePairs(Table table, object instance, InstanceCreationOptions creationOptions)
        {
            var membersThatNeedToBeSet = GetMembersThatNeedToBeSet(table, instance.GetType());
            var memberHandlers = membersThatNeedToBeSet.ToList();
            var memberNames = memberHandlers.Select(h => h.MemberName);

            VerifyAllColumn(table, creationOptions, memberNames);

            memberHandlers.ForEach(x => x.Setter(instance, x.GetValue()));
        }

        private static void VerifyAllColumn(Table table, InstanceCreationOptions creationOptions, IEnumerable<string> memberNames)
        {
            if (creationOptions?.VerifyAllColumnsBound == true)
            {
                var memberNameKeys = new HashSet<string>(memberNames);
                var allIds = table.Rows.Select(r => r.Id()).ToList();
                var missing = allIds.Where(m => !memberNameKeys.Contains(m)).ToList();
                if (missing.Any())
                {
                    throw new ColumnCouldNotBeBoundException(missing);
                }
            }
        }

        internal static List<MemberHandler> GetMembersThatNeedToBeSet(Table table, Type type)

        {
            var properties = (from property in type.GetProperties()
                              from row in table.Rows
                              where TheseTypesMatch(type, property.PropertyType, row)
                                    && IsMemberMatchingToColumnName(property, row.Id())
                              select new MemberHandler { Type = type, Row = row, MemberName = property.Name, PropertyType = property.PropertyType, Setter = (i, v) => property.SetValue(i, v, null) }).ToList();

            var fieldInfos = type.GetFields();
            var fields = (from field in fieldInfos
                          from row in table.Rows
                          where TheseTypesMatch(type, field.FieldType, row)
                                && IsMemberMatchingToColumnName(field, row.Id())
                          select new MemberHandler { Type = type, Row = row, MemberName = field.Name, PropertyType = field.FieldType, Setter = (i, v) => field.SetValue(i, v) }).ToList();

            var memberHandlers = new List<MemberHandler>(properties.Capacity + fields.Count);

            memberHandlers.AddRange(properties);
            memberHandlers.AddRange(fields);

            // tuple special case
            if (IsValueTupleType(type))
            {
                if (fieldInfos.Length > 7)
                {
                    throw new Exception("You should just map to tuple with small objects, types with more than 7 properties are not currently supported");
                }

                if (fieldInfos.Length == table.RowCount)
                {
                    for (var index = 0; index < table.Rows.Count; index++)
                    {
                        var field = fieldInfos[index];
                        var row = table.Rows[index];

                        if (TheseTypesMatch(type, field.FieldType, row))
                        {
                            memberHandlers.Add(new MemberHandler
                            {
                                Type = type,
                                Row = row,
                                MemberName = field.Name,
                                PropertyType = field.FieldType,
                                Setter = (i, v) => field.SetValue(i, v)
                            });
                        }
                    }
                }
            }

            return memberHandlers;
        }

        private static bool IsMatchingAlias(MemberInfo field, string id)
        {
            var aliases = field.GetCustomAttributes().OfType<TableAliasesAttribute>();
            return aliases.Any(a => a.Aliases.Any(al => a.UseExactMatch ? id == al : Regex.Match(id, al).Success));
        }

        private static bool TheseTypesMatch(Type targetType, Type memberType, DataTableRow row)
        {
            return Service.Instance.GetValueRetrieverFor(row, targetType, memberType) != null;
        }

        internal class MemberHandler
        {
            public DataTableRow Row { get; set; }
            public string MemberName { get; set; }
            public Action<object, object> Setter { get; set; }
            public Type Type { get; set; }
            public Type PropertyType { get; set; }

            public object GetValue()
            {
                var valueRetriever = Service.Instance.GetValueRetrieverFor(Row, Type, PropertyType);
                return valueRetriever.Retrieve(new KeyValuePair<string, string>(Row[0], Row[1]), Type, PropertyType);
            }
        }

        internal static Table GetTheProperInstanceTable(Table table, Type type)
        {
            return ThisIsAVerticalTable(table, type)
                ? table
                : FlipThisHorizontalTableToAVerticalTable(table);
        }

        private static Table FlipThisHorizontalTableToAVerticalTable(Table table)
        {
            return new PivotTable(table).GetInstanceTable(0);
        }

        private static bool ThisIsAVerticalTable(Table table, Type type)
        {
            if (TheHeaderIsTheOldFieldValuePair(table))
                return true;
            return (table.Rows.Count() != 1) || (table.Header.Count == 2 && TheFirstRowValueIsTheNameOfAProperty(table, type));
        }

        private static bool TheHeaderIsTheOldFieldValuePair(Table table)
        {
            return table.Header.Count == 2 && table.Header.First() == "Field" && table.Header.Last() == "Value";
        }

        private static bool TheFirstRowValueIsTheNameOfAProperty(Table table, Type type)
        {
            var firstRowValue = table.Rows[0][table.Header.First()];
            return type.GetProperties()
                       .Any(property => IsMemberMatchingToColumnName(property, firstRowValue));
        }

        public static bool IsValueTupleType(Type type, bool checkBaseTypes = false)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (type == typeof(Tuple))
                return true;

            while (type != null)
            {
                if (type.IsGenericType)
                {
                    var genType = type.GetGenericTypeDefinition();
                    if
                    (
                        genType == typeof(ValueTuple)
                        || genType == typeof(ValueTuple<>)
                        || genType == typeof(ValueTuple<,>)
                        || genType == typeof(ValueTuple<,,>)
                        || genType == typeof(ValueTuple<,,,>)
                        || genType == typeof(ValueTuple<,,,,>)
                        || genType == typeof(ValueTuple<,,,,,>)
                        || genType == typeof(ValueTuple<,,,,,,>)
                        || genType == typeof(ValueTuple<,,,,,,,>)
                    )
                        return true;
                }

                if (!checkBaseTypes)
                    break;

                type = type.BaseType;
            }

            return false;
        }
    }
}
