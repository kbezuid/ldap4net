using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using LdapForNet.Native;
using LdapForNet.Utils;
using Encoder = LdapForNet.Utils.Encoder;

namespace LdapForNet
{
    public enum ExtendedDNFlag
    {
        HexString = 0,
        StandardString = 1
    }

    [Flags]
    public enum SecurityMasks
    {
        None = 0,
        Owner = 1,
        Group = 2,
        Dacl = 4,
        Sacl = 8
    }

    [Flags]
    public enum DirectorySynchronizationOptions : long
    {
        None = 0,
        ObjectSecurity = 0x1,
        ParentsFirst = 0x0800,
        PublicDataOnly = 0x2000,
        IncrementalValues = 0x80000000
    }

    public enum SearchOption
    {
        DomainScope = 1,
        PhantomRoot = 2
    }

    [StructLayout(LayoutKind.Sequential)]

    internal class SortKeyNative
    {
        internal IntPtr AttributeName { get; set; }
        internal IntPtr MatchingRule { get; set; }
        internal bool ReverseOrder { get; set; }

    }

    public class SortKey
    {
        private string _name;

        public SortKey()
        {
        }

        public SortKey(string attributeName, string matchingRule, bool reverseOrder)
        {
            AttributeName = attributeName;
            MatchingRule = matchingRule;
            ReverseOrder = reverseOrder;
        }

        public string AttributeName
        {
            get => _name;
            set => _name = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string MatchingRule { get; set; }

        public bool ReverseOrder { get; set; }

        internal SortKeyNative ToNative()
        {
            return new SortKeyNative
            {
                AttributeName = Encoder.Instance.StringToPtr(_name),
                MatchingRule = string.IsNullOrEmpty(MatchingRule) ? IntPtr.Zero : Encoder.Instance.StringToPtr(MatchingRule),
                ReverseOrder = ReverseOrder
            };
        }
    }

    public class DirectoryControl
    {
        internal byte[] _directoryControlValue;

        public DirectoryControl(string type, byte[] value, bool isCritical, bool serverSide)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));

            if (value != null)
            {
                _directoryControlValue = new byte[value.Length];
                Array.Copy(value,_directoryControlValue,value.Length);
            }
            IsCritical = isCritical;
            ServerSide = serverSide;
        }

        public virtual byte[] GetValue()
        {
            if (_directoryControlValue == null)
            {
                return Array.Empty<byte>();
            }

            return _directoryControlValue.Copy();
        }

        public string Type { get; }

        public bool IsCritical { get; set; }

        public bool ServerSide { get; set; }

        internal static void TransformControls(DirectoryControl[] controls)
        {
            for (var i = 0; i < controls.Length; i++)
            {
                Debug.Assert(controls[i] != null);
                var value = controls[i].GetValue();
                if (controls[i].Type == "1.2.840.113556.1.4.319")
                {
                    // The control is a PageControl.
                    var result = BerConverter.Decode("{iO}", value);
                    Debug.Assert((result != null) && (result.Length == 2));

                    var size = (int)result[0];
                    // user expects cookie with length 0 as paged search is done.
                    var cookie = (byte[])result[1] ?? Array.Empty<byte>();

                    var pageControl = new PageResultResponseControl(size, cookie, controls[i].IsCritical, controls[i].GetValue());
                    controls[i] = pageControl;
                }
                else if (controls[i].Type == "1.2.840.113556.1.4.1504")
                {
                    // The control is an AsqControl.
                    var o = BerConverter.Decode("{e}", value);
                    Debug.Assert((o != null) && (o.Length == 1));

                    var result = (int)o[0];
                    var asq = new AsqResponseControl(result, controls[i].IsCritical, controls[i].GetValue());
                    controls[i] = asq;
                }
                else if (controls[i].Type == "1.2.840.113556.1.4.841")
                {
                    // The control is a DirSyncControl.
                    var o = BerConverter.Decode("{iiO}", value);
                    Debug.Assert(o != null && o.Length == 3);

                    var moreData = (int)o[0];
                    var count = (int)o[1];
                    var dirsyncCookie = (byte[])o[2];

                    var dirsync = new DirSyncResponseControl(dirsyncCookie, (moreData == 0 ? false : true), count, controls[i].IsCritical, controls[i].GetValue());
                    controls[i] = dirsync;
                }
                else if (controls[i].Type == "1.2.840.113556.1.4.474")
                {
                    // The control is a SortControl.
                    var result = 0;
                    string attribute = null;
                    var o = BerConverter.TryDecode("{ea}", value, out var decodeSucceeded);

                    // decode might fail as AD for example never returns attribute name, we don't want to unnecessarily throw and catch exception
                    if (decodeSucceeded)
                    {
                        Debug.Assert(o != null && o.Length == 2);
                        result = (int)o[0];
                        attribute = (string)o[1];
                    }
                    else
                    {
                        // decoding might fail as attribute is optional
                        o = BerConverter.Decode("{e}", value);
                        Debug.Assert(o != null && o.Length == 1);

                        result = (int)o[0];
                    }

                    var sort = new SortResponseControl((Native.Native.ResultCode)result, attribute, controls[i].IsCritical, controls[i].GetValue());
                    controls[i] = sort;
                }
                else if (controls[i].Type == "2.16.840.1.113730.3.4.10")
                {
                    // The control is a VlvResponseControl.
                    int position;
                    int count;
                    int result;
                    byte[] context = null;
                    var o = BerConverter.TryDecode("{iieO}", value, out var decodeSucceeded);

                    if (decodeSucceeded)
                    {
                        Debug.Assert(o != null && o.Length == 4);
                        position = (int)o[0];
                        count = (int)o[1];
                        result = (int)o[2];
                        context = (byte[])o[3];
                    }
                    else
                    {
                        o = BerConverter.Decode("{iie}", value);
                        Debug.Assert(o != null && o.Length == 3);
                        position = (int)o[0];
                        count = (int)o[1];
                        result = (int)o[2];
                    }

                    var vlv = new VlvResponseControl(position, count, context, (Native.Native.ResultCode)result, controls[i].IsCritical, controls[i].GetValue());
                    controls[i] = vlv;
                }
            }
        }
    }

    /// <summary>
    /// Attribute scoped query control (Stateless)
    /// https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/77d880bf-aadd-4f6f-bb78-076af8e22cd8
    /// </summary>
    public class AsqRequestControl : DirectoryControl
    {
        public AsqRequestControl() : base("1.2.840.113556.1.4.1504", null, true, true)
        {
        }

        public AsqRequestControl(string attributeName) : this()
        {
            AttributeName = attributeName;
        }

        public string AttributeName { get; set; }

        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{s}", new object[] { AttributeName });
            return base.GetValue();
        }
    }

    public class AsqResponseControl : DirectoryControl
    {
        internal AsqResponseControl(int result, bool criticality, byte[] controlValue) : base("1.2.840.113556.1.4.1504", controlValue, criticality, true)
        {
            Result = (Native.Native.ResultCode)result;
        }

        public Native.Native.ResultCode Result { get; }
    }

    public class CrossDomainMoveControl : DirectoryControl
    {
        public CrossDomainMoveControl() : base("1.2.840.113556.1.4.521", null, true, true)
        {
        }

        public CrossDomainMoveControl(string targetDomainController) : this()
        {
            TargetDomainController = targetDomainController;
        }

        public string TargetDomainController { get; set; }

        public override byte[] GetValue()
        {
            if (TargetDomainController != null)
            {
                var encoder = new UTF8Encoding();
                var bytes = encoder.GetBytes(TargetDomainController);

                // Allocate large enough space for the '\0' character.
                _directoryControlValue = bytes.Copy(bytes.Length + 2);
            }
            return base.GetValue();
        }
    }

    public class DomainScopeControl : DirectoryControl
    {
        public DomainScopeControl() : base("1.2.840.113556.1.4.1339", null, true, true)
        {
        }
    }

    public class ExtendedDNControl : DirectoryControl
    {
        private ExtendedDNFlag _flag = ExtendedDNFlag.HexString;

        public ExtendedDNControl() : base("1.2.840.113556.1.4.529", null, true, true)
        {
        }

        public ExtendedDNControl(ExtendedDNFlag flag) : this()
        {
            Flag = flag;
        }

        public ExtendedDNFlag Flag
        {
            get => _flag;
            set
            {
                if (value < ExtendedDNFlag.HexString || value > ExtendedDNFlag.StandardString)
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ExtendedDNFlag));

                _flag = value;
            }
        }
        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{i}", new object[] { (int)Flag });
            return base.GetValue();
        }
    }

    public class LazyCommitControl : DirectoryControl
    {
        public LazyCommitControl() : base("1.2.840.113556.1.4.619", null, true, true) { }
    }

    public class DirectoryNotificationControl : DirectoryControl
    {
        public DirectoryNotificationControl() : base("1.2.840.113556.1.4.528", null, true, true) { }
    }

    public class PermissiveModifyControl : DirectoryControl
    {
        public PermissiveModifyControl() : base("1.2.840.113556.1.4.1413", null, true, true) { }
    }

    public class SecurityDescriptorFlagControl : DirectoryControl
    {
        public SecurityDescriptorFlagControl() : base("1.2.840.113556.1.4.801", null, true, true) { }

        public SecurityDescriptorFlagControl(SecurityMasks masks) : this()
        {
            SecurityMasks = masks;
        }

        // We don't do validation to the dirsync flag here as underneath API does not check for it and we don't want to put
        // unnecessary limitation on it.
        public SecurityMasks SecurityMasks { get; set; }

        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{i}", new object[] { (int)SecurityMasks });
            return base.GetValue();
        }
    }

    public class SearchOptionsControl : DirectoryControl
    {
        private SearchOption _searchOption = SearchOption.DomainScope;
        public SearchOptionsControl() : base("1.2.840.113556.1.4.1340", null, true, true) { }

        public SearchOptionsControl(SearchOption flags) : this()
        {
            SearchOption = flags;
        }

        public SearchOption SearchOption
        {
            get => _searchOption;
            set
            {
                if (value < SearchOption.DomainScope || value > SearchOption.PhantomRoot)
                    throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SearchOption));

                _searchOption = value;
            }
        }

        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{i}", new object[] { (int)SearchOption });
            return base.GetValue();
        }
    }

    public class ShowDeletedControl : DirectoryControl
    {
        public ShowDeletedControl() : base("1.2.840.113556.1.4.417", null, true, true) { }
    }

    public class TreeDeleteControl : DirectoryControl
    {
        public TreeDeleteControl() : base("1.2.840.113556.1.4.805", null, true, true) { }
    }

    public class VerifyNameControl : DirectoryControl
    {
        private string _serverName;

        public VerifyNameControl() : base("1.2.840.113556.1.4.1338", null, true, true) { }

        public VerifyNameControl(string serverName) : this()
        {
            _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
        }

        public VerifyNameControl(string serverName, int flag) : this(serverName)
        {
            Flag = flag;
        }

        public string ServerName
        {
            get => _serverName;
            set => _serverName = value ?? throw new ArgumentNullException(nameof(value));
        }

        public int Flag { get; set; }

        public override byte[] GetValue()
        {
            byte[] tmpValue = null;
            if (ServerName != null)
            {
                var unicode = new UnicodeEncoding();
                tmpValue = unicode.GetBytes(ServerName);
            }

            _directoryControlValue = BerConverter.Encode("{io}", new object[] { Flag, tmpValue });
            return base.GetValue();
        }
    }

    public class DirSyncRequestControl : DirectoryControl
    {
        private byte[] _dirSyncCookie;
        private int _count = 1048576;

        public DirSyncRequestControl() : base("1.2.840.113556.1.4.841", null, true, true) { }
        public DirSyncRequestControl(byte[] cookie) : this()
        {
            _dirSyncCookie = cookie;
        }

        public DirSyncRequestControl(byte[] cookie, DirectorySynchronizationOptions option) : this(cookie)
        {
            Option = option;
        }

        public DirSyncRequestControl(byte[] cookie, DirectorySynchronizationOptions option, int attributeCount) : this(cookie, option)
        {
            AttributeCount = attributeCount;
        }

        public byte[] Cookie
        {
            get
            {
                if (_dirSyncCookie == null)
                {
                    return Array.Empty<byte>();
                }

                return _dirSyncCookie.Copy();
            }
            set => _dirSyncCookie = value;
        }

        // We don't do validation to the dirsync flag here as underneath API does not check for it and we don't want to put
        // unnecessary limitation on it.
        public DirectorySynchronizationOptions Option { get; set; }

        public int AttributeCount
        {
            get => _count;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Not valid value", nameof(value));
                }

                _count = value;
            }
        }

        public override byte[] GetValue()
        {
            var o = new object[] { (int)Option, AttributeCount, _dirSyncCookie };
            _directoryControlValue = BerConverter.Encode("{iio}", o);
            return base.GetValue();
        }
    }

    public class DirSyncResponseControl : DirectoryControl
    {
        private readonly byte[] _dirSyncCookie;

        internal DirSyncResponseControl(byte[] cookie, bool moreData, int resultSize, bool criticality, byte[] controlValue) : base("1.2.840.113556.1.4.841", controlValue, criticality, true)
        {
            _dirSyncCookie = cookie;
            MoreData = moreData;
            ResultSize = resultSize;
        }

        public byte[] Cookie
        {
            get
            {
                if (_dirSyncCookie == null)
                {
                    return Array.Empty<byte>();
                }

                return _dirSyncCookie.Copy();
            }
        }

        public bool MoreData { get; }

        public int ResultSize { get; }
    }

    public class PageResultRequestControl : DirectoryControl
    {
        private int _size = 512;
        private byte[] _pageCookie;

        public PageResultRequestControl() : base("1.2.840.113556.1.4.319", null, true, true) { }

        public PageResultRequestControl(int pageSize) : this()
        {
            PageSize = pageSize;
        }

        public PageResultRequestControl(byte[] cookie) : this()
        {
            _pageCookie = cookie;
        }

        public int PageSize
        {
            get => _size;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Not valid value", nameof(value));
                }

                _size = value;
            }
        }

        public byte[] Cookie
        {
            get
            {
                if (_pageCookie == null)
                {
                    return Array.Empty<byte>();
                }

                return _pageCookie.Copy();
            }
            set => _pageCookie = value;
        }

        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{io}", PageSize, _pageCookie);
            return base.GetValue();
        }
    }

    public class PageResultResponseControl : DirectoryControl
    {
        private byte[] _pageCookie;

        internal PageResultResponseControl(int count, byte[] cookie, bool criticality, byte[] controlValue) : base("1.2.840.113556.1.4.319", controlValue, criticality, true)
        {
            TotalCount = count;
            _pageCookie = cookie;
        }

        public byte[] Cookie
        {
            get
            {
                if (_pageCookie == null)
                {
                    return Array.Empty<byte>();
                }

                return _pageCookie.Copy();
            }
        }

        public int TotalCount { get; }
    }

    public class SortRequestControl : DirectoryControl
    {
        private SortKey[] _keys = Array.Empty<SortKey>();
        public SortRequestControl(params SortKey[] sortKeys) : base("1.2.840.113556.1.4.473", null, true, true)
        {
            if (sortKeys == null)
            {
                throw new ArgumentNullException(nameof(sortKeys));
            }

            if (sortKeys.Any(_ => _ == null))
            {
                throw new ArgumentException("Found null in array", nameof(sortKeys));
            }

            _keys = new SortKey[sortKeys.Length];
            for (var i = 0; i < sortKeys.Length; i++)
            {
                _keys[i] = new SortKey(sortKeys[i].AttributeName, sortKeys[i].MatchingRule, sortKeys[i].ReverseOrder);
            }
        }

        public SortRequestControl(string attributeName, bool reverseOrder) : this(attributeName, null, reverseOrder)
        {
        }

        public SortRequestControl(string attributeName, string matchingRule, bool reverseOrder) : base("1.2.840.113556.1.4.473", null, true, true)
        {
            var key = new SortKey(attributeName, matchingRule, reverseOrder);
            _keys = new[] { key };
        }

        public SortKey[] SortKeys
        {
            get
            {
                if (_keys == null)
                {
                    return Array.Empty<SortKey>();
                }

                var tempKeys = new SortKey[_keys.Length];
                for (var i = 0; i < _keys.Length; i++)
                {
                    tempKeys[i] = new SortKey(_keys[i].AttributeName, _keys[i].MatchingRule, _keys[i].ReverseOrder);
                }
                return tempKeys;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (value.Any(_ => _ == null))
                {
                    throw new ArgumentException("Found null value in array", nameof(value));
                }

                _keys = new SortKey[value.Length];
                for (var i = 0; i < value.Length; i++)
                {
                    _keys[i] = new SortKey(value[i].AttributeName, value[i].MatchingRule, value[i].ReverseOrder);
                }
            }
        }

        public override byte[] GetValue()
        {
            var control = IntPtr.Zero;
            var structSize = Marshal.SizeOf(typeof(SortKeyNative));
            var nativeKeys = _keys
                .Select(_ => _.ToNative())
                .Select(key =>
            {
                var res = Marshal.AllocHGlobal(structSize);
                Marshal.StructureToPtr(key, res, false);
                return res;
            });

            var memHandle = IntPtr.Zero;

            try
            {
                memHandle = MarshalUtils.WriteIntPtrArray(nativeKeys.ToArray());

                var critical = IsCritical;
                var ld = IntPtr.Zero;
                LdapNative.Instance.Init(ref ld, null);
                var ldapHandle = new LdapHandle(ld);
                var error = LdapNative.Instance.ldap_create_sort_control(ldapHandle, memHandle, critical ? (byte)1 : (byte)0, ref control);
                
                LdapNative.Instance.ThrowIfError(error,nameof(LdapNative.Instance.ldap_create_sort_control));

                var managedControl = new Native.Native.LdapControl();
                Marshal.PtrToStructure(control, managedControl);
                var value = managedControl.ldctl_value;

                // reinitialize the value
                _directoryControlValue = null;
                if (value != null)
                {
                    _directoryControlValue = new byte[value.bv_len];
                    Marshal.Copy(value.bv_val, _directoryControlValue, 0, value.bv_len);
                }
            }
            finally
            {
                if (control != IntPtr.Zero)
                {
                    LdapNative.Instance.ldap_control_free(control);
                }

                if (memHandle != IntPtr.Zero)
                {
                    //release the memory from the heap
                    foreach (var tempPtr in MarshalUtils.GetPointerArray(memHandle))
                    {
                        // free the marshalled name
                        var ptr = Marshal.ReadIntPtr(tempPtr);
                        if (ptr != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(ptr);
                        }
                        // free the marshalled rule
                        ptr = Marshal.ReadIntPtr(tempPtr, IntPtr.Size);
                        if (ptr != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(ptr);
                        }

                        Marshal.FreeHGlobal(tempPtr);
                    }
                    
                    Marshal.FreeHGlobal(memHandle);
                }
            }

            return base.GetValue();
        }
    }

    public class SortResponseControl : DirectoryControl
    {
        internal SortResponseControl(Native.Native.ResultCode result, string attributeName, bool critical, byte[] value) : base("1.2.840.113556.1.4.474", value, critical, true)
        {
            Result = result;
            AttributeName = attributeName;
        }

        public Native.Native.ResultCode Result { get; }

        public string AttributeName { get; }
    }

    public class VlvRequestControl : DirectoryControl
    {
        private int _before = 0;
        private int _after = 0;
        private int _offset = 0;
        private int _estimateCount = 0;
        private byte[] _target;
        private byte[] _context;

        public VlvRequestControl() : base("2.16.840.1.113730.3.4.9", null, true, true) { }

        public VlvRequestControl(int beforeCount, int afterCount, int offset) : this()
        {
            BeforeCount = beforeCount;
            AfterCount = afterCount;
            Offset = offset;
        }

        public VlvRequestControl(int beforeCount, int afterCount, string target) : this()
        {
            BeforeCount = beforeCount;
            AfterCount = afterCount;
            if (target != null)
            {
                _target = Encoder.Instance.GetBytes(target);
            }
        }

        public VlvRequestControl(int beforeCount, int afterCount, byte[] target) : this()
        {
            BeforeCount = beforeCount;
            AfterCount = afterCount;
            Target = target;
        }

        public int BeforeCount
        {
            get => _before;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Is not valid value", nameof(value));
                }

                _before = value;
            }
        }

        public int AfterCount
        {
            get => _after;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Is not valid value", nameof(value));
                }

                _after = value;
            }
        }

        public int Offset
        {
            get => _offset;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Is not valid value", nameof(value));
                }

                _offset = value;
            }
        }

        public int EstimateCount
        {
            get => _estimateCount;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Is not valid value", nameof(value));
                }

                _estimateCount = value;
            }
        }

        public byte[] Target
        {
            get
            {
                if (_target == null)
                {
                    return Array.Empty<byte>();
                }

                return _target.Copy();
            }
            set => _target = value;
        }

        public byte[] ContextId
        {
            get
            {
                if (_context == null)
                {
                    return Array.Empty<byte>();
                }

                return _context.Copy();
            }
            set => _context = value;
        }

        public override byte[] GetValue()
        {
            var seq = new StringBuilder(10);
            var objList = new ArrayList();

            // first encode the before and the after count.
            seq.Append("{ii");
            objList.Add(BeforeCount);
            objList.Add(AfterCount);

            // encode Target if it is not null
            if (Target.Length != 0)
            {
                seq.Append("t");
                objList.Add(0x80 | 0x1);
                seq.Append("o");
                objList.Add(Target);
            }
            else
            {
                seq.Append("t{");
                objList.Add(0xa0);
                seq.Append("ii");
                objList.Add(Offset);
                objList.Add(EstimateCount);
                seq.Append("}");
            }

            // encode the contextID if present
            if (ContextId.Length != 0)
            {
                seq.Append("o");
                objList.Add(ContextId);
            }

            seq.Append("}");
            var values = new object[objList.Count];
            for (var i = 0; i < objList.Count; i++)
            {
                values[i] = objList[i];
            }

            _directoryControlValue = BerConverter.Encode(seq.ToString(), objList.ToArray());
            return base.GetValue();
        }
    }

    /// <summary>
    /// https://docs.microsoft.com/en-us/previous-versions/windows/desktop/ldap/searching-with-the-ldap-vlv-control
    /// </summary>
    public class VlvResponseControl : DirectoryControl
    {
        private byte[] _context;

        internal VlvResponseControl(int targetPosition, int count, byte[] context, Native.Native.ResultCode result, bool criticality, byte[] value) : base("2.16.840.1.113730.3.4.10", value, criticality, true)
        {
            TargetPosition = targetPosition;
            ContentCount = count;
            _context = context;
            Result = result;
        }

        public int TargetPosition { get; }

        public int ContentCount { get; }

        public byte[] ContextId
        {
            get
            {
                if (_context == null)
                {
                    return Array.Empty<byte>();
                }

                return _context.Copy();
            }
        }

        public Native.Native.ResultCode Result { get; }
    }

    public class QuotaControl : DirectoryControl
    {
        public QuotaControl() : base("1.2.840.113556.1.4.1852", null, true, true) { }

        public QuotaControl(byte[] querySid) : this()
        {
            QuerySid = querySid;
        }

        public byte[] QuerySid { get; set; }

        public override byte[] GetValue()
        {
            _directoryControlValue = BerConverter.Encode("{o}", QuerySid);
            return base.GetValue();
        }
    }

    internal static class ArrayExtensions
    {
        public static T[] Copy<T>(this T[] array, int length = 0)
        {
            length = length > 0 ? length : array.Length;
            var res = new T[length];
            Array.Copy(array,res,array.Length);
            return res;
        }
    }
}